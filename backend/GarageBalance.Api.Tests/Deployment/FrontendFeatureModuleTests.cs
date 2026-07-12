namespace GarageBalance.Api.Tests.Deployment;

public sealed class FrontendFeatureModuleTests
{
    [Fact]
    public void FundsPanelRemainsInItsFeatureModule()
    {
        var repositoryRoot = FindRepositoryRoot();
        var appText = File.ReadAllText(Path.Combine(repositoryRoot, "frontend", "src", "App.tsx"));
        var fundsPanelText = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "frontend",
            "src",
            "features",
            "funds",
            "FundsPanel.tsx"));
        var roadmapLine = File
            .ReadLines(Path.Combine(repositoryRoot, "docs", "project-roadmap.md"))
            .TakeWhile(line => !string.Equals(line, "## История выполнения", StringComparison.Ordinal))
            .Single(line => line.Contains("Frontend разделять на feature-модули", StringComparison.Ordinal));

        Assert.Contains("import { FundsPrototypePanel } from './features/funds/FundsPanel'", appText, StringComparison.Ordinal);
        Assert.Contains("<FundsPrototypePanel", appText, StringComparison.Ordinal);
        Assert.DoesNotContain("function FundsPrototypePanel(", appText, StringComparison.Ordinal);
        Assert.DoesNotContain("type FundOperationDraft", appText, StringComparison.Ordinal);
        Assert.DoesNotContain("mapFundDtoToPrototypeRow", appText, StringComparison.Ordinal);

        Assert.Contains("export function FundsPrototypePanel(", fundsPanelText, StringComparison.Ordinal);
        Assert.Contains("fundsClient.createOperation", fundsPanelText, StringComparison.Ordinal);
        Assert.Contains("fundsClient.updateOperation", fundsPanelText, StringComparison.Ordinal);
        Assert.Contains("fundsClient.cancelOperation", fundsPanelText, StringComparison.Ordinal);
        Assert.Contains("fundsClient.restoreOperation", fundsPanelText, StringComparison.Ordinal);
        Assert.Contains("operationReverse.operation.fundId", fundsPanelText, StringComparison.Ordinal);
        Assert.Contains("frontend/src/features/funds/FundsPanel.tsx", roadmapLine, StringComparison.Ordinal);
    }

    [Fact]
    public void PasswordPanelRemainsInItsFeatureModuleWithSharedFormField()
    {
        var repositoryRoot = FindRepositoryRoot();
        var appText = File.ReadAllText(Path.Combine(repositoryRoot, "frontend", "src", "App.tsx"));
        var passwordPanelText = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "frontend",
            "src",
            "features",
            "settings",
            "PasswordPanel.tsx"));
        var formFieldText = File.ReadAllText(Path.Combine(repositoryRoot, "frontend", "src", "shared", "FormField.tsx"));
        var roadmapLine = File
            .ReadLines(Path.Combine(repositoryRoot, "docs", "project-roadmap.md"))
            .TakeWhile(line => !string.Equals(line, "## История выполнения", StringComparison.Ordinal))
            .Single(line => line.Contains("Frontend разделять на feature-модули", StringComparison.Ordinal));

        Assert.Contains("import { PasswordPanel } from './features/settings/PasswordPanel'", appText, StringComparison.Ordinal);
        Assert.Contains("<PasswordPanel", appText, StringComparison.Ordinal);
        Assert.DoesNotContain("function PasswordPanel(", appText, StringComparison.Ordinal);
        Assert.DoesNotContain("getOneCFreshSyncConfirmationTitle", appText, StringComparison.Ordinal);
        Assert.DoesNotContain("function FormField(", appText, StringComparison.Ordinal);

        Assert.Contains("export function PasswordPanel(", passwordPanelText, StringComparison.Ordinal);
        Assert.Contains("authClient.changeOwnPassword", passwordPanelText, StringComparison.Ordinal);
        Assert.Contains("integrationClient.previewOneCFreshSync", passwordPanelText, StringComparison.Ordinal);
        Assert.Contains("integrationClient.updateProtectedSetting", passwordPanelText, StringComparison.Ordinal);
        Assert.Contains("hasPermission(auth, permissions.usersManage)", passwordPanelText, StringComparison.Ordinal);
        Assert.Contains("export function FormField(", formFieldText, StringComparison.Ordinal);
        Assert.Contains("frontend/src/features/settings/PasswordPanel.tsx", roadmapLine, StringComparison.Ordinal);
        Assert.Contains("shared UI", roadmapLine, StringComparison.Ordinal);
    }

    [Fact]
    public void AuthGateRemainsInItsFeatureModule()
    {
        var repositoryRoot = FindRepositoryRoot();
        var appText = File.ReadAllText(Path.Combine(repositoryRoot, "frontend", "src", "App.tsx"));
        var authGateText = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "frontend",
            "src",
            "features",
            "auth",
            "AuthGate.tsx"));
        var roadmapLine = File
            .ReadLines(Path.Combine(repositoryRoot, "docs", "project-roadmap.md"))
            .TakeWhile(line => !string.Equals(line, "## История выполнения", StringComparison.Ordinal))
            .Single(line => line.Contains("Frontend разделять на feature-модули", StringComparison.Ordinal));

        Assert.Contains("import { AuthGate } from './features/auth/AuthGate'", appText, StringComparison.Ordinal);
        Assert.Contains("<AuthGate authClient={authClient} onAuthenticated={handleAuthenticated} />", appText, StringComparison.Ordinal);
        Assert.DoesNotContain("function AuthGate(", appText, StringComparison.Ordinal);
        Assert.DoesNotContain("getAuthValidationErrors", appText, StringComparison.Ordinal);

        Assert.Contains("export function AuthGate(", authGateText, StringComparison.Ordinal);
        Assert.Contains("getAuthValidationErrors('login'", authGateText, StringComparison.Ordinal);
        Assert.Contains("await authClient.login({ email, password })", authGateText, StringComparison.Ordinal);
        Assert.Contains("aria-label=\"Вход в систему\"", authGateText, StringComparison.Ordinal);
        Assert.Contains("frontend/src/features/auth/AuthGate.tsx", roadmapLine, StringComparison.Ordinal);
    }

    [Fact]
    public void ReleasePanelRemainsInItsFeatureModule()
    {
        var repositoryRoot = FindRepositoryRoot();
        var appText = File.ReadAllText(Path.Combine(repositoryRoot, "frontend", "src", "App.tsx"));
        var releasePanelText = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "frontend",
            "src",
            "features",
            "releases",
            "ReleasePanel.tsx"));
        var roadmapLine = File
            .ReadLines(Path.Combine(repositoryRoot, "docs", "project-roadmap.md"))
            .TakeWhile(line => !string.Equals(line, "## История выполнения", StringComparison.Ordinal))
            .Single(line => line.Contains("Frontend разделять на feature-модули", StringComparison.Ordinal));

        Assert.Contains("import { ReleasePanel } from './features/releases/ReleasePanel'", appText, StringComparison.Ordinal);
        Assert.Contains("<ReleasePanel", appText, StringComparison.Ordinal);
        Assert.DoesNotContain("function ReleasePanel(", appText, StringComparison.Ordinal);
        Assert.DoesNotContain("createReleaseEditorState", appText, StringComparison.Ordinal);

        Assert.Contains("export function ReleasePanel(", releasePanelText, StringComparison.Ordinal);
        Assert.Contains("releaseClient.getReleases", releasePanelText, StringComparison.Ordinal);
        Assert.Contains("releaseClient.getManageableReleases", releasePanelText, StringComparison.Ordinal);
        Assert.Contains("hasPermission(auth, permissions.appReleasesManage)", releasePanelText, StringComparison.Ordinal);
        Assert.Contains("aria-label=\"Что нового\"", releasePanelText, StringComparison.Ordinal);
        Assert.Contains("frontend/src/features/releases/ReleasePanel.tsx", roadmapLine, StringComparison.Ordinal);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "GarageBalance.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new DirectoryNotFoundException("Repository root was not found.");
    }
}
