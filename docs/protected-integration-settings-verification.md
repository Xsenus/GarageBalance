# Protected Integration Settings Verification

Документ фиксирует evidence для Stage 4 пункта шифрования runtime-настроек интеграций.

## Backend

- [x] `IntegrationSecretCatalog` разрешает только `OneCFresh:RefreshToken`, `ReceiptPrinting:DeviceConnection`, `ReceiptPrinting:ReceiptTemplate`.
- [x] `IntegrationSecretSettingsService` отклоняет неизвестные ключи, шифрует plaintext через отдельный purpose и хранит только `gb:protected:v1:` ciphertext.
- [x] `PUT /api/integrations/settings/{provider}/{settingKey}` защищен `users.manage`, принимает новое значение и возвращает только metadata.
- [x] Audit показывает provider, key, purpose и состояние `задано/обновлено`, но не plaintext.
- [x] Data Protection keys сохраняются в постоянном локальном/VPS каталоге или Docker volume.

## Frontend

- [x] Администратор может задать или заменить refresh token, подключение устройства и шаблон квитанции.
- [x] Поля очищаются после успешного сохранения; сохраненное значение нельзя прочитать обратно.
- [x] Статус сразу обновляет число настроенных параметров по metadata.
- [x] Пользователь без `users.manage` видит разрешенный статус интеграции, но не формы управления секретами.

## Проверки

- [x] `IntegrationSecretSettingsServiceTests` покрывает encryption, case-insensitive update, no-op, validation, allowlist, metadata и отсутствие plaintext в audit.
- [x] `IntegrationsControllerTests` покрывает success, actor, metadata-only response, missing body и validation error.
- [x] `ControllerAuthorizationCoverageTests` закрепляет `users.manage`.
- [x] React и `integrationsApi` тесты покрывают PUT body, очистку полей, metadata status и permission-denied UI.
- [x] Release `0.539.0` объясняет механизм администратору без раскрытия внутренних значений.
