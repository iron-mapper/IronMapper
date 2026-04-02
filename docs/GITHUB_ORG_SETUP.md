# GitHub Organization Setup Guide

> ⚠️ **Внутренний документ** — не для публичного репозитория.
> Описывает ручные настройки GitHub, которые нельзя автоматизировать через код.

---

## 1. Защита ветки main (Branch Protection Rules)

Перейди: **Organization → IronMapper → Settings → Branches → Add rule**

| Настройка | Значение |
|---|---|
| Branch name pattern | `main` |
| ✅ Require a pull request before merging | включить |
| &nbsp;&nbsp;✅ Require approvals | **1** |
| &nbsp;&nbsp;✅ Dismiss stale pull request approvals when new commits are pushed | включить |
| ✅ Require status checks to pass before merging | включить |
| &nbsp;&nbsp;Required checks | `Build & Test (ubuntu-latest)`, `Build & Test (windows-latest)` |
| &nbsp;&nbsp;✅ Require branches to be up to date before merging | включить |
| ✅ Require conversation resolution before merging | включить |
| ✅ Do not allow bypassing the above settings | включить |
| ❌ Allow force pushes | выключить |
| ❌ Allow deletions | выключить |

---

## 2. Настройки репозитория

Перейди: **Settings → General**

### About (описание в правой колонке)

- **Description:**
  ```
  Zero-reflection, compile-time object mapper for .NET 10.
  Free alternative to AutoMapper. By Iron Programmer School.
  ```
- **Website:** ссылка на NuGet-пакет: `https://www.nuget.org/packages/IronMapper`
- **Topics (теги):** добавить все следующие:
  ```
  dotnet  csharp  mapper  automapper  source-generator
  compile-time  object-mapping  dto  nuget  dotnet10
  ```

### Features

- ✅ **Preserve this repository** (Sponsorships)
- ❌ **Wiki** — выключить (документация живёт в `docs/`)
- ❌ **Projects** — выключить (не используем пока)

---

## 3. Настройки организации

Перейди: **Organization Settings → Member privileges**

| Настройка | Значение |
|---|---|
| Base permissions | **Read** |
| Allow members to create repositories | Только **приватные** |
| Allow forking of private repositories | **Выключить** |

---

## 4. Секреты организации

Перейди: **Organization Settings → Secrets → Actions**

### Перенос NUGET_API_KEY

Сейчас `NUGET_API_KEY` задан на уровне репозитория. Перенести на уровень организации, чтобы работал для всех будущих пакетов Iron Programmer:

1. Скопируй текущее значение из: **IronMapper → Settings → Secrets → NUGET_API_KEY**
2. Создай секрет организации: **Organization Settings → Secrets → Actions → New organization secret**
   - Name: `NUGET_API_KEY`
   - Value: (вставь скопированное значение)
   - **Repository access:** Selected repositories → добавить `IronMapper`
3. Удали старый секрет из репозитория: **IronMapper → Settings → Secrets → NUGET_API_KEY → Delete**

> **Почему:** Один секрет на уровне организации будет автоматически доступен любому новому пакету Iron Programmer без повторной настройки.

---

## 5. Страница организации (.github репозиторий)

### Создать репозиторий

Создать публичный репозиторий `iron-mapper/.github` со следующей структурой:

```
.github/
└── profile/
    └── README.md
```

### Содержимое `profile/README.md`

```markdown
## 🔩 Iron Programmer School

Открытые инструменты для .NET разработчиков от школы Iron Programmer.

### Проекты

| Проект | Описание | NuGet |
|--------|----------|-------|
| [IronMapper](https://github.com/iron-mapper/IronMapper) | Compile-time object mapper для .NET 10 | [![NuGet](https://img.shields.io/nuget/v/IronMapper)](https://www.nuget.org/packages/IronMapper) |

### Ссылки
- 🌐 [Школа Iron Programmer](#)
- 💬 [Сообщество](#)
- 📦 [NuGet профиль](https://www.nuget.org/profiles/iron-mapper)
```

> GitHub автоматически показывает `profile/README.md` на странице организации.

---

## 6. Checklist перед публичным релизом

- [ ] Branch protection rules настроены для `main`
- [ ] Description и Topics выставлены в настройках репозитория
- [ ] `NUGET_API_KEY` перенесён на уровень организации
- [ ] Репозиторий `iron-mapper/.github` создан с `profile/README.md`
- [ ] Wiki и Projects отключены
- [ ] Тег `v1.0.0` создан для запуска CI publish workflow
- [ ] Пакеты появились на `https://www.nuget.org/packages/IronMapper`
