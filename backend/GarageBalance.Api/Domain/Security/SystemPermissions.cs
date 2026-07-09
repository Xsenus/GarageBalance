namespace GarageBalance.Api.Domain.Security;

public static class SystemPermissions
{
    public const string UsersManage = "users.manage";
    public const string DictionariesRead = "dictionaries.read";
    public const string DictionariesWrite = "dictionaries.write";
    public const string TariffsManage = "tariffs.manage";
    public const string PaymentsRead = "payments.read";
    public const string PaymentsWrite = "payments.write";
    public const string ReportsRead = "reports.read";
    public const string ImportRun = "import.run";
    public const string AppReleasesManage = "app_releases.manage";
    public const string AuditRead = "audit.read";

    public static readonly string[] Administrator =
    [
        UsersManage,
        DictionariesRead,
        DictionariesWrite,
        TariffsManage,
        PaymentsRead,
        PaymentsWrite,
        ReportsRead,
        ImportRun,
        AppReleasesManage,
        AuditRead
    ];

    public static readonly string[] Accountant =
    [
        DictionariesRead,
        DictionariesWrite,
        TariffsManage,
        PaymentsRead,
        PaymentsWrite,
        ReportsRead,
        ImportRun
    ];

    public static readonly string[] Operator =
    [
        DictionariesRead,
        PaymentsRead,
        PaymentsWrite
    ];

    public static readonly string[] ReportsViewer =
    [
        DictionariesRead,
        ReportsRead
    ];

    public static readonly string[] All =
    [
        UsersManage,
        DictionariesRead,
        DictionariesWrite,
        TariffsManage,
        PaymentsRead,
        PaymentsWrite,
        ReportsRead,
        ImportRun,
        AppReleasesManage,
        AuditRead
    ];
}
