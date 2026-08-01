# Module File Standard Verification

검증 커밋:

```text
1d5c98e64875b0d99a5c43007d1a9f875a2663d5
```

Windows CI:

```text
ChartKit CSharp Engine
run 30684840018
result success

ChartKit CSharp Legacy Inventory
run 30684840019
result success
```

핵심 검사 단계:

```text
Verify chart module file contracts
result success
```

현재 기능군 Module 프로젝트가 아직 생성되지 않았으므로 검사 결과는 `module_files=0`이다. 첫 `ChartKit.Modules.*` 프로젝트와 `*Module.cs` 파일이 추가되는 즉시 다음 항목이 강제된다.

```text
파일 첫 80줄의 <chart-module> 계약
필수 메타데이터 13개
파일명과 Module-Class 일치
IChartModule 구현
ChartModuleDefinition Definition
ModuleDefinition 공개
표준 Renderer-Path
SkiaSharp 직접 참조 금지
WinForms Control 직접 참조 금지
```
