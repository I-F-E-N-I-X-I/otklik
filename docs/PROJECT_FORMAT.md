# Формат проекта v1

UTF-8 JSON, camelCase. Полный пример — samples/demo-project.json.

Project: schemaVersion=1, id, name, port, cors (bool), routes.
Route: id, enabled, method, path, responses, mode (rules/sequence), fallbackResponseId, sequence (массив ID), endBehavior (holdLast/loop).
Response: id, name, statusCode, contentType, headers (объект), body (строка), delayMs, enabled, conditions.
Condition: source (query/header/body/path), key, operator (equals/contains/exists), value (строка).

ID сохраняются при записи. Счётчики сценариев в файл не входят. Неизвестная версия отвергается. Сохранение записывает временный файл в тот же каталог и заменяет целевой файл.

Отклонения от концептуальной схемы плана: CORS представлен bool allow-all без credentials; sequence/endBehavior — плоские поля маршрута. Это реальный формат schemaVersion 1.
