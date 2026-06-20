# Task 1: 프로젝트 스캐폴드 (CodeWiki 코어 ETL)

이것이 너의 요구사항이다. 아래 명령·값을 **그대로(verbatim)** 사용하라.

## 목표
.NET 10 솔루션과 두 프로젝트(실행 + 테스트)를 스캐폴드하고 필요한 NuGet 패키지를 추가한다. 빌드·테스트가 실행되는 상태로 만든다.

## Files
- Create: `src/CodeWiki/CodeWiki.csproj`, `src/CodeWiki.Tests/CodeWiki.Tests.csproj`, `CodeWiki.sln` (저장소 루트)

## Step 1: 솔루션·프로젝트 생성
```bash
dotnet new sln -n CodeWiki
dotnet new console -n CodeWiki -o src/CodeWiki -f net10.0
dotnet new xunit -n CodeWiki.Tests -o src/CodeWiki.Tests -f net10.0
dotnet sln add src/CodeWiki/CodeWiki.csproj src/CodeWiki.Tests/CodeWiki.Tests.csproj
dotnet add src/CodeWiki.Tests/CodeWiki.Tests.csproj reference src/CodeWiki/CodeWiki.csproj
```

## Step 2: 패키지 추가
```bash
dotnet add src/CodeWiki/CodeWiki.csproj package Buildalyzer
dotnet add src/CodeWiki/CodeWiki.csproj package Buildalyzer.Workspaces
dotnet add src/CodeWiki/CodeWiki.csproj package Microsoft.CodeAnalysis.CSharp.Workspaces
dotnet add src/CodeWiki/CodeWiki.csproj package Neo4j.Driver
dotnet add src/CodeWiki.Tests/CodeWiki.Tests.csproj package Microsoft.CodeAnalysis.CSharp
```

## Step 3: 빌드·테스트 동작 확인
Run: `dotnet build && dotnet test`
Expected: 빌드 성공, 테스트 0개 통과(또는 xunit 템플릿이 만든 기본 테스트 1개 통과).

## Step 4: Commit
```bash
git add CodeWiki.sln src/
git commit -m "chore(codewiki): net10 솔루션·프로젝트 스캐폴드 + 패키지"
```

## 완료 기준 (이 태스크의 deliverable)
- `CodeWiki.sln`에 `src/CodeWiki`(console, net10.0)와 `src/CodeWiki.Tests`(xunit, net10.0)가 등록됨.
- 테스트 프로젝트가 실행 프로젝트를 참조.
- 5개 패키지 추가됨(실행 4 + 테스트 1).
- `dotnet build && dotnet test` 성공.
- 위 메시지로 커밋 1개 생성.

## 제약 (Global Constraints)
- **타깃 net10.0** (두 프로젝트 모두).
- 이 태스크는 스캐폴드만. 도메인 코드(Pk/Node/Graph 등)는 다음 태스크 — **여기서 만들지 마라.**
- 패키지 버전은 명시하지 않음 → `dotnet add package`가 net10 호환 최신을 가져오게 둔다. 복원 실패 시 그 패키지의 net10 호환 최신 안정 버전을 명시.
