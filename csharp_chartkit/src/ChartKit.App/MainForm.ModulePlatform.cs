using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using ChartKit.CSharp.Contracts;
using ChartKit.CSharp.ModuleHost;
using ChartKit.CSharp.Modules.Abstractions;
using ChartKit.CSharp.Persistence;
using ChartKit.CSharp.Scene;
using ChartKit.CSharp.UiModel;

namespace ChartKit.CSharp.App;

internal sealed partial class MainForm
{
    private readonly System.Windows.Forms.Timer _profileSaveTimer = new();
    private readonly TabControl _rightTabs = new();
    private readonly TabPage _infoTab = new("종목정보");
    private readonly TabPage _moduleTab = new("모듈");
    private readonly Panel _moduleInspectorHost = new();
    private readonly TableLayoutPanel _moduleInspectorTable = new();
    private readonly ToolStripStatusLabel _moduleStatusLabel = new();
    private readonly List<ToolStripItem> _moduleQuickItems = [];
    private readonly List<ToolStripItem> _moduleContextItems = [];
    private ChartModulePlatformController? _modulePlatform;
    private ChartRenderPlan _moduleRenderPlan =
        new(Array.Empty<RenderPrimitivePlan>());
    private long _modulePlanDataVersion = -1;
    private bool _modulePlatformReady;
    private bool _applyingChartProfile;
    private bool _savingChartProfile;

    private ChartModulePlatformController ModulePlatform =>
        _modulePlatform ?? throw new InvalidOperationException(
            "Chart module platform controller is unavailable.");

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        _modulePlatform = new ChartModulePlatformController(_options.ProfilePath);
        InitializeModuleInspectorShell();
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        BeginInvoke(async () => await InitializeModulePlatformAfterInitialLoadAsync());
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        FlushModuleProfileOnClose();
        base.OnFormClosing(e);
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        base.OnFormClosed(e);
        _profileSaveTimer.Dispose();
        _modulePlatform?.Dispose();
    }

    private async Task InitializeModulePlatformAfterInitialLoadAsync()
    {
        try
        {
            await _reloadGate.WaitAsync(_stop.Token);
            _reloadGate.Release();

            CandleTimeframe profileTimeframe =
                await InitializeModulePlatformAsync();
            if (profileTimeframe != _workspace.Timeframe)
            {
                await ReloadAsync(profileTimeframe);
            }
            else if (TryGetSelectedSnapshot(out Engine.SymbolSnapshot? snapshot))
            {
                _viewport.SetVisibleBars(
                    _workspace.RequestedVisibleBars,
                    snapshot.Candles.Length,
                    followLatest: true);
            }

            SynchronizeShellChecks();
            _chart.Invalidate();
        }
        catch (OperationCanceledException) when (_stop.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            ShowFailure("Chart Profile 시작 실패", exception);
        }
    }

    private async Task<CandleTimeframe> InitializeModulePlatformAsync()
    {
        ChartProfile profile = await ModulePlatform.InitializeAsync(
            _workspace.Timeframe.ToString(),
            _stop.Token);

        _applyingChartProfile = true;
        try
        {
            ApplyShellProfile(profile);
        }
        finally
        {
            _applyingChartProfile = false;
        }

        _modulePlatformReady = true;
        _moduleRenderPlan = ModulePlatform.RenderPlan;

        ChartUiCatalogSnapshot catalog = ModulePlatform.BuildUiCatalog();
        ChartUiCommandItem? firstModule = catalog.ContextMenuItems.FirstOrDefault(
            static item => item.Kind == ChartUiCommandKind.ModuleToggle);
        if (firstModule is not null)
            ModulePlatform.Select(firstModule.Owner);

        RefreshModuleUi();
        UpdateModuleStatus();
        return AppOptions.TryParseTimeframe(
            profile.Timeframe,
            out CandleTimeframe timeframe)
            ? timeframe
            : _workspace.Timeframe;
    }

    private void InitializeModuleInspectorShell()
    {
        _workspaceSplit.Panel2.Controls.Remove(_infoTable);

        _rightTabs.Dock = DockStyle.Fill;
        _rightTabs.Alignment = TabAlignment.Top;

        _infoTab.Padding = new Padding(0);
        _moduleTab.Padding = new Padding(0);
        _infoTab.Controls.Add(_infoTable);

        _moduleInspectorHost.Dock = DockStyle.Fill;
        _moduleInspectorHost.AutoScroll = true;
        _moduleInspectorHost.BackColor = Color.FromArgb(24, 28, 34);
        _moduleInspectorTable.AutoSize = true;
        _moduleInspectorTable.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        _moduleInspectorTable.Dock = DockStyle.Top;
        _moduleInspectorTable.Padding = new Padding(8);
        _moduleInspectorTable.ColumnCount = 2;
        _moduleInspectorTable.ColumnStyles.Add(
            new ColumnStyle(SizeType.Absolute, 88f));
        _moduleInspectorTable.ColumnStyles.Add(
            new ColumnStyle(SizeType.Percent, 100f));
        _moduleInspectorHost.Controls.Add(_moduleInspectorTable);
        _moduleTab.Controls.Add(_moduleInspectorHost);

        _rightTabs.TabPages.Add(_infoTab);
        _rightTabs.TabPages.Add(_moduleTab);
        _workspaceSplit.Panel2.Controls.Add(_rightTabs);

        _moduleStatusLabel.BorderSides = ToolStripStatusLabelBorderSides.Left;
        _moduleStatusLabel.Text = "modules loading";
        _moduleStatusLabel.AutoSize = true;
        _statusStrip.Items.Add(_moduleStatusLabel);

        _profileSaveTimer.Interval = 350;
        _profileSaveTimer.Tick += async (_, _) =>
        {
            _profileSaveTimer.Stop();
            await SaveShellProfileAsync();
        };

        _chartContextMenu.Opening += (_, _) => RefreshModuleContextMenu();
        _chartContextMenu.ItemClicked += (_, _) => ScheduleModuleProfileSave();
        _toolsButton.DropDownItemClicked += (_, _) => ScheduleModuleProfileSave();
        _dateButton.Click += (_, _) => ScheduleModuleProfileSave();
        _infoButton.Click += (_, _) => ScheduleModuleProfileSave();
        _timeframeSelector.SelectedIndexChanged +=
            (_, _) => ScheduleModuleProfileSave();
        _visibleBarsSelector.SelectedIndexChanged +=
            (_, _) => ScheduleModuleProfileSave();
        _frameTimer.Tick += (_, _) => RefreshModulePlanForCurrentSnapshot();
    }

    private void RefreshModuleUi()
    {
        if (!_modulePlatformReady) return;

        ChartUiCatalogSnapshot catalog = ModulePlatform.BuildUiCatalog();
        RefreshModuleQuickToolbar(catalog);
        RefreshModuleContextMenu(catalog);
        RefreshModuleInspector(catalog);
        UpdateModuleStatus();
    }

    private void RefreshModuleQuickToolbar(ChartUiCatalogSnapshot catalog)
    {
        RemoveDynamicItems(_toolbar.Items, _moduleQuickItems);
        if (catalog.QuickToolbarItems.Count == 0) return;

        var separator = new ToolStripSeparator();
        _toolbar.Items.Add(separator);
        _moduleQuickItems.Add(separator);

        foreach (ChartUiCommandItem command in catalog.QuickToolbarItems)
        {
            var button = new ToolStripButton(command.DisplayName)
            {
                AutoSize = true,
                CheckOnClick = false,
                Checked = command.IsCheckable && command.IsChecked,
                Enabled = command.IsEnabled,
                ToolTipText = $"{command.Category} / {command.CommandId}",
                Tag = command
            };
            button.Click += async (_, _) =>
                await ExecuteModuleUiCommandAsync(command);
            _toolbar.Items.Add(button);
            _moduleQuickItems.Add(button);
        }
    }

    private void RefreshModuleContextMenu()
    {
        if (!_modulePlatformReady) return;
        RefreshModuleContextMenu(ModulePlatform.BuildUiCatalog());
    }

    private void RefreshModuleContextMenu(ChartUiCatalogSnapshot catalog)
    {
        RemoveDynamicItems(_chartContextMenu.Items, _moduleContextItems);
        if (catalog.ContextMenuItems.Count == 0) return;

        var separator = new ToolStripSeparator();
        _chartContextMenu.Items.Add(separator);
        _moduleContextItems.Add(separator);

        foreach (IGrouping<string, ChartUiCommandItem> category in
                 catalog.ContextMenuItems.GroupBy(
                     static item => item.Category,
                     StringComparer.Ordinal))
        {
            var categoryItem = new ToolStripMenuItem(category.Key);
            foreach (ChartUiCommandItem command in category)
            {
                var item = new ToolStripMenuItem(command.DisplayName)
                {
                    CheckOnClick = false,
                    Checked = command.IsCheckable && command.IsChecked,
                    Enabled = command.IsEnabled,
                    Tag = command
                };
                item.Click += async (_, _) =>
                    await ExecuteModuleUiCommandAsync(command);
                categoryItem.DropDownItems.Add(item);
            }

            _chartContextMenu.Items.Add(categoryItem);
            _moduleContextItems.Add(categoryItem);
        }
    }

    private async Task ExecuteModuleUiCommandAsync(ChartUiCommandItem command)
    {
        try
        {
            ChartModulePlatformActionResult result =
                await ModulePlatform.ExecuteCommandAsync(
                    command,
                    _stop.Token);
            if (!result.Succeeded)
            {
                _statusLabel.Text =
                    "모듈 명령 실패: " + (result.Error ?? "unknown error");
                return;
            }

            _moduleRenderPlan = ModulePlatform.RenderPlan;
            _rightTabs.SelectedTab = _moduleTab;
            RefreshModuleUi();
            _chart.Invalidate();
            _statusLabel.Text = result.Changed
                ? $"모듈 변경 완료: {command.DisplayName}"
                : $"모듈 선택: {command.DisplayName}";
        }
        catch (OperationCanceledException) when (_stop.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            ShowFailure("모듈 명령 실패", exception);
        }
    }

    private void RefreshModuleInspector(ChartUiCatalogSnapshot catalog)
    {
        _moduleInspectorTable.SuspendLayout();
        try
        {
            while (_moduleInspectorTable.Controls.Count > 0)
            {
                Control control = _moduleInspectorTable.Controls[0];
                _moduleInspectorTable.Controls.RemoveAt(0);
                control.Dispose();
            }
            _moduleInspectorTable.RowStyles.Clear();
            _moduleInspectorTable.RowCount = 0;

            if (!catalog.Selection.HasValue)
            {
                AddInspectorMessage("차트 또는 모듈을 선택하세요.");
                return;
            }

            if (catalog.InspectorProperties.Count == 0)
            {
                AddInspectorMessage("선택한 모듈에 편집 가능한 속성이 없습니다.");
                return;
            }

            ChartUiPropertyItem first = catalog.InspectorProperties[0];
            AddInspectorHeader(
                first.ModuleDisplayName,
                first.IsModuleEnabled ? "활성" : "비활성");

            foreach (ChartUiPropertyItem property in catalog.InspectorProperties)
                AddInspectorProperty(property);
        }
        finally
        {
            _moduleInspectorTable.ResumeLayout(performLayout: true);
        }
    }

    private void AddInspectorHeader(string title, string state)
    {
        int row = AddInspectorRow(34f);
        var label = new Label
        {
            Text = title,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            Font = new Font(Font, FontStyle.Bold),
            ForeColor = Color.Gainsboro
        };
        var stateLabel = new Label
        {
            Text = state,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleRight,
            ForeColor = Color.DarkGray
        };
        _moduleInspectorTable.Controls.Add(label, 0, row);
        _moduleInspectorTable.Controls.Add(stateLabel, 1, row);
    }

    private void AddInspectorMessage(string message)
    {
        int row = AddInspectorRow(44f);
        var label = new Label
        {
            Text = message,
            Dock = DockStyle.Fill,
            AutoSize = true,
            ForeColor = Color.DarkGray,
            TextAlign = ContentAlignment.MiddleLeft
        };
        _moduleInspectorTable.Controls.Add(label, 0, row);
        _moduleInspectorTable.SetColumnSpan(label, 2);
    }

    private void AddInspectorProperty(ChartUiPropertyItem property)
    {
        int row = AddInspectorRow(31f);
        var caption = new Label
        {
            Text = property.Descriptor.DisplayName,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = Color.DarkGray,
            AutoEllipsis = true
        };
        Control editor = CreatePropertyEditor(property);
        _moduleInspectorTable.Controls.Add(caption, 0, row);
        _moduleInspectorTable.Controls.Add(editor, 1, row);
    }

    private int AddInspectorRow(float height)
    {
        int row = _moduleInspectorTable.RowCount++;
        _moduleInspectorTable.RowStyles.Add(
            new RowStyle(SizeType.Absolute, height));
        return row;
    }

    private Control CreatePropertyEditor(ChartUiPropertyItem property)
    {
        ChartPropertyDescriptor descriptor = property.Descriptor;
        bool enabled = property.IsEditable;

        switch (descriptor.ValueKind)
        {
            case ChartPropertyValueKind.Boolean:
            {
                var checkBox = new CheckBox
                {
                    Dock = DockStyle.Fill,
                    Checked = descriptor.Value is bool value && value,
                    Enabled = enabled
                };
                checkBox.CheckedChanged += async (_, _) =>
                    await ApplyModulePropertyAsync(
                        property,
                        JsonValue.Create(checkBox.Checked));
                return checkBox;
            }
            case ChartPropertyValueKind.Integer:
            case ChartPropertyValueKind.Decimal:
            {
                var numeric = new NumericUpDown
                {
                    Dock = DockStyle.Fill,
                    DecimalPlaces = descriptor.ValueKind ==
                        ChartPropertyValueKind.Integer ? 0 : 4,
                    Increment = descriptor.ValueKind ==
                        ChartPropertyValueKind.Integer ? 1m : 0.1m,
                    Minimum = ToDecimalBound(descriptor.Minimum, -1_000_000_000m),
                    Maximum = ToDecimalBound(descriptor.Maximum, 1_000_000_000m),
                    Enabled = enabled
                };
                decimal current = Convert.ToDecimal(
                    descriptor.Value ?? 0,
                    CultureInfo.InvariantCulture);
                numeric.Value = Math.Clamp(
                    current,
                    numeric.Minimum,
                    numeric.Maximum);
                numeric.Validated += async (_, _) =>
                {
                    JsonNode? node = descriptor.ValueKind ==
                        ChartPropertyValueKind.Integer
                        ? JsonValue.Create(decimal.ToInt32(numeric.Value))
                        : JsonValue.Create(decimal.ToDouble(numeric.Value));
                    await ApplyModulePropertyAsync(property, node);
                };
                return numeric;
            }
            case ChartPropertyValueKind.Enum:
            {
                var combo = new ComboBox
                {
                    Dock = DockStyle.Fill,
                    DropDownStyle = ComboBoxStyle.DropDownList,
                    Enabled = enabled
                };
                combo.Items.AddRange(
                    descriptor.AllowedValues.Cast<object>().ToArray());
                combo.SelectedItem = Convert.ToString(
                    descriptor.Value,
                    CultureInfo.InvariantCulture);
                combo.SelectedIndexChanged += async (_, _) =>
                {
                    if (combo.SelectedItem is string value)
                    {
                        await ApplyModulePropertyAsync(
                            property,
                            JsonValue.Create(value));
                    }
                };
                return combo;
            }
            case ChartPropertyValueKind.DateRange:
            case ChartPropertyValueKind.Collection:
            {
                return new TextBox
                {
                    Dock = DockStyle.Fill,
                    ReadOnly = true,
                    Text = descriptor.Value is null
                        ? string.Empty
                        : JsonSerializer.Serialize(descriptor.Value),
                    Enabled = enabled
                };
            }
            default:
            {
                var text = new TextBox
                {
                    Dock = DockStyle.Fill,
                    Text = Convert.ToString(
                        descriptor.Value,
                        CultureInfo.InvariantCulture) ?? string.Empty,
                    ReadOnly = !enabled
                };
                text.Validated += async (_, _) =>
                    await ApplyModulePropertyAsync(
                        property,
                        JsonValue.Create(text.Text));
                return text;
            }
        }
    }

    private async Task ApplyModulePropertyAsync(
        ChartUiPropertyItem property,
        JsonNode? value)
    {
        try
        {
            ChartPropertyChangeResult result =
                await ModulePlatform.ChangePropertyAsync(
                    property.Owner.InstanceId,
                    property.Descriptor.PropertyId,
                    value,
                    _stop.Token);
            if (!result.Succeeded)
            {
                _statusLabel.Text =
                    "속성 변경 실패: " + (result.Error ?? "unknown error");
                RefreshModuleUi();
                return;
            }

            _moduleRenderPlan = ModulePlatform.RenderPlan;
            RefreshModuleUi();
            _chart.Invalidate();
            _statusLabel.Text = result.Changed
                ? $"속성 변경: {property.Descriptor.DisplayName} " +
                  $"({result.ChangeImpact})"
                : $"속성 유지: {property.Descriptor.DisplayName}";
        }
        catch (OperationCanceledException) when (_stop.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            ShowFailure("모듈 속성 변경 실패", exception);
        }
    }

    private void ApplyShellProfile(ChartProfile profile)
    {
        JsonObject layout = profile.Layout;
        JsonObject interaction = profile.Interaction;

        if (TryReadInt(layout, "visibleBars", out int visibleBars) &&
            visibleBars > 0)
        {
            _workspace.SetVisibleBars(visibleBars);
        }
        if (TryReadBool(layout, "infoPanelVisible", out bool infoVisible))
            _workspace.SetInfoPanel(infoVisible);
        if (TryReadBool(interaction, "datesVisible", out bool datesVisible))
            _workspace.SetDates(datesVisible);
        if (TryReadBool(interaction, "axesVisible", out bool axesVisible))
            _workspace.SetAxes(axesVisible);
        if (TryReadBool(interaction, "legendVisible", out bool legendVisible))
            _workspace.SetLegend(legendVisible);
        if (TryReadBool(
                interaction,
                "crosshairVisible",
                out bool crosshairVisible))
        {
            _workspace.SetCrosshair(crosshairVisible);
        }

        SynchronizeShellChecks();
    }

    private void ScheduleModuleProfileSave()
    {
        if (!_modulePlatformReady ||
            _applyingChartProfile ||
            Volatile.Read(ref _closing) != 0)
        {
            return;
        }

        _profileSaveTimer.Stop();
        _profileSaveTimer.Start();
    }

    private async Task SaveShellProfileAsync()
    {
        if (!_modulePlatformReady || _savingChartProfile) return;

        _savingChartProfile = true;
        try
        {
            await ModulePlatform.UpdateShellProfileAsync(
                _workspace.Timeframe.ToString(),
                CaptureLayoutProfile(),
                CaptureInteractionProfile(),
                new JsonObject(),
                _stop.Token);
        }
        catch (OperationCanceledException) when (_stop.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            _statusLabel.Text = "Profile 저장 실패: " + exception.Message;
        }
        finally
        {
            _savingChartProfile = false;
        }
    }

    private void FlushModuleProfileOnClose()
    {
        _profileSaveTimer.Stop();
        if (!_modulePlatformReady) return;

        try
        {
            ModulePlatform.UpdateShellProfileAsync(
                    _workspace.Timeframe.ToString(),
                    CaptureLayoutProfile(),
                    CaptureInteractionProfile(),
                    new JsonObject(),
                    CancellationToken.None)
                .GetAwaiter()
                .GetResult();
        }
        catch (Exception exception)
        {
            Debug.WriteLine("Chart profile close save failed: " + exception);
        }
    }

    private JsonObject CaptureLayoutProfile() =>
        new()
        {
            ["visibleBars"] = _workspace.RequestedVisibleBars,
            ["infoPanelVisible"] = _workspace.ShowInfoPanel
        };

    private JsonObject CaptureInteractionProfile() =>
        new()
        {
            ["datesVisible"] = _workspace.ShowDates,
            ["axesVisible"] = _workspace.ShowAxes,
            ["legendVisible"] = _workspace.ShowLegend,
            ["crosshairVisible"] = _workspace.ShowCrosshair
        };

    private void RefreshModulePlanForCurrentSnapshot()
    {
        if (!_modulePlatformReady ||
            !TryGetSelectedSnapshot(out Engine.SymbolSnapshot? snapshot) ||
            snapshot.Version == _modulePlanDataVersion)
        {
            return;
        }

        _modulePlanDataVersion = snapshot.Version;
        ModulePlatform.Recompose(snapshot.Version);
        _moduleRenderPlan = ModulePlatform.RenderPlan;
        UpdateModuleStatus();
    }

    private void UpdateModuleStatus()
    {
        _moduleStatusLabel.Text = GetModulePlatformSummary();
    }

    private string GetModulePlatformSummary()
    {
        if (!_modulePlatformReady) return "modules loading";
        IReadOnlyList<ChartModuleRuntimeSnapshot> snapshots =
            ModulePlatform.GetSnapshots();
        int enabled = snapshots.Count(static item => item.IsEnabled);
        int faulted = snapshots.Count(static item => item.IsFaulted);
        return $"modules {enabled}/{snapshots.Count} " +
               $"plan {_moduleRenderPlan.Primitives.Count} faults {faulted}";
    }

    private static void RemoveDynamicItems(
        ToolStripItemCollection collection,
        List<ToolStripItem> items)
    {
        foreach (ToolStripItem item in items)
        {
            collection.Remove(item);
            item.Dispose();
        }
        items.Clear();
    }

    private static decimal ToDecimalBound(
        double? value,
        decimal fallback)
    {
        if (!value.HasValue) return fallback;
        if (value.Value >= (double)decimal.MaxValue) return decimal.MaxValue;
        if (value.Value <= (double)decimal.MinValue) return decimal.MinValue;
        return Convert.ToDecimal(value.Value, CultureInfo.InvariantCulture);
    }

    private static bool TryReadBool(
        JsonObject source,
        string key,
        out bool value)
    {
        if (source[key] is JsonValue json && json.TryGetValue(out value))
            return true;
        value = default;
        return false;
    }

    private static bool TryReadInt(
        JsonObject source,
        string key,
        out int value)
    {
        if (source[key] is JsonValue json && json.TryGetValue(out value))
            return true;
        value = default;
        return false;
    }
}
