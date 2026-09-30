# Project instructions

When this repository belongs to the registered D:\Agent Workspaces workspace, read its AGENTS.md and .workspace/projects.json first. Workspace rules are maintained there.

- Run powershell -ExecutionPolicy Bypass -File .\build.ps1 -Test before releasing.
- Preserve .NET Framework compiler compatibility and IWorkbenchTool contract.
- Keep real credentials and server profiles exclusively in the canonical user data directory, never in Git or releases.
- Maintain main, immutable version tags, and one cumulative formal Release page. Preserve prior commits, tags, release notes and actual assets.
- Keep AssemblyInfo.cs, UI and README versions consistent; package.ps1 reads the binary version.
- Publish only the explicit package allowlist and verify hashes after downloading uploaded assets.
- CI uses fixtures. Real network tests require authorization and must not compete for active forwarding ports.

- If deploy.local.json exists, build.ps1 -Test synchronizes the tested daily installation and preserves user data. Use -SkipDeploy only for isolated verification; complete normal deployment before delivering an app update.

- DataLocation.Root uses the user profile (.desktop-workbench) to avoid packaged-host AppData redirection. Validate ordinary desktop/task-service launches, not only development-host launches. Preserve old data during migration.
- Keep the daily on-demand launch task at limited interactive permissions, with no triggers, no execution time limit, and no battery termination. It must be independent of the development terminal.
