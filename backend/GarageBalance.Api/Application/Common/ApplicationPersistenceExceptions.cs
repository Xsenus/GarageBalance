namespace GarageBalance.Api.Application.Common;

public sealed class ApplicationConcurrencyException(Exception innerException)
    : Exception("Запись была изменена другим пользователем.", innerException);

public sealed class ApplicationPersistenceConflictException(Exception innerException)
    : Exception("Изменение нарушает ограничение данных.", innerException);
