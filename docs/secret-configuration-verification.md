# External Secret Configuration Verification

Документ фиксирует evidence для Stage 4 пункта хранения секретов вне репозитория.

## Каналы

- [x] Development user-secrets включены через `UserSecretsId`; команды задают JWT, PostgreSQL connection string и путь Data Protection keys.
- [x] Environment configuration использует `Jwt__SigningKey`, `ConnectionStrings__DefaultConnection` и `DataProtection__KeysPath`.
- [x] Docker Compose требует `POSTGRES_PASSWORD` и `JWT_SIGNING_KEY` из приватного `.env`, а Data Protection keys хранит в volume `data-protection-keys`.
- [x] VPS использует root-managed `/etc/garagebalance-staging.env` и постоянный каталог `/var/lib/garagebalance-staging/data-protection-keys` вне release-каталогов.
- [x] Локальная установка без Docker использует `C:\GarageBalance\Config` и отдельный постоянный каталог `DataProtectionKeys`.

## Защита

- [x] JWT startup validator блокирует пустые, короткие и примерные production secrets.
- [x] `.gitignore`, privacy script и `SensitiveFileGitIgnoreTests` блокируют реальные `.env`, дампы, Access-файлы и приватные импорты.
- [x] Runtime-токены интеграций не относятся к configuration secrets: они сохраняются зашифрованными через `IIntegrationSecretSettingsService` и проверяются следующим roadmap-пунктом.
- [x] `SecretConfigurationTests` и `ExternalSecretConfigurationIsCompleteWhenUserSecretsEnvironmentDeploymentAndPersistentKeysExist` связывают конфигурацию, документацию и roadmap.

## Acceptance Notes

- [x] Новая запись "Что нового" не нужна: пользовательское поведение и бизнес-правила не менялись.
- [x] Живая Docker/PostgreSQL проверка выполняется при наличии окружения; без него обязательны compose tests, idempotent EF script и полный test/build контур.
