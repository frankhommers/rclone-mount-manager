# IsPassword-Only Secret UX Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Voeg voor alle rclone-opties met `IsPassword=true` een masked input met oogje en verificatieveld toe, zonder extra secret-detectie buiten `IsPassword`.

**Architecture:** We hergebruiken de bestaande `Text` control-flow en maken passwordgedrag conditioneel op `IRcloneOptionDefinition.IsPassword`. Validatie blijft in ViewModel-laag zodat de XAML alleen presentatielogica bevat. Inclusion (`ShouldInclude`) wordt geblokkeerd bij verificatie-mismatch om ongeldige secrets uit scriptgeneratie te houden.

**Tech Stack:** .NET 10, Avalonia UI (XAML), CommunityToolkit.Mvvm

---

### Task 1: Add password-specific state and validation in typed option VM

**Files:**
- Modify: `RcloneMountManager.Core/ViewModels/TypedOptionViewModel.cs`
- Test: `RcloneMountManager.Tests/**` (nieuwe of bestaande ViewModel tests)

**Step 1: Write the failing test**

Maak tests voor:
- `IsPassword=true` + mismatch confirm => `ShouldInclude == false`
- `IsPassword=true` + match confirm => `ShouldInclude` volgt bestaande rules
- `IsPassword=false` => gedrag onveranderd

**Step 2: Run test to verify it fails**

Run: `dotnet test --filter FullyQualifiedName~TypedOptionViewModel`
Expected: FAIL op ontbrekende nieuwe password/confirm-logica.

**Step 3: Write minimal implementation**

Voeg toe in `TypedOptionViewModel`:
- `public bool IsPassword => Option.IsPassword;`
- `ConfirmValue` property
- `IsSecretVisible` property + toggle command
- `PasswordsMatch`/`HasPasswordMismatch` computed properties
- `ShouldInclude` conditioneel uitbreiden: bij `IsPassword` alleen include als geen mismatch

Zorg dat property notifications consistent zijn bij wijziging van `Value` en `ConfirmValue`.

**Step 4: Run test to verify it passes**

Run: `dotnet test --filter FullyQualifiedName~TypedOptionViewModel`
Expected: PASS.

**Step 5: Commit**

```bash
git add RcloneMountManager.Core/ViewModels/TypedOptionViewModel.cs RcloneMountManager.Tests
git commit -m "feat: add IsPassword confirmation and visibility state"
```

### Task 2: Update Text template to render masked secret UI

**Files:**
- Modify: `RcloneMountManager.GUI/App.axaml`

**Step 1: Write the failing test**

Als UI-tests ontbreken, definieer verificatiestap als handmatige acceptance:
- IsPassword-optie toont twee velden + oogje
- Niet-IsPassword blijft enkel normaal tekstveld

**Step 2: Run test to verify it fails**

Run: app handmatig met een bekende `IsPassword` optie (bijv. `rc_pass`).
Expected: huidig scherm mist confirmatie/oogje-gedrag.

**Step 3: Write minimal implementation**

Pas `Text` template conditioneel aan:
- `IsPassword=false`: bestaande `TextBox` houden.
- `IsPassword=true`: primary + confirm veld renderen met password-char gedrag en oogje-knop.
- Inline fouttekst tonen bij mismatch.

**Step 4: Run test to verify it passes**

Herhaal handmatige check met `rc_pass` of andere `IsPassword` optie.
Expected: beide velden + zichtbaarheid-toggle + mismatch feedback zichtbaar en werkend.

**Step 5: Commit**

```bash
git add RcloneMountManager.GUI/App.axaml
git commit -m "feat: render IsPassword fields with eye toggle and confirmation"
```

### Task 3: Verify end-to-end inclusion behavior

**Files:**
- Verify: `RcloneMountManager.GUI/ViewModels/MountOptionsViewModel.cs`
- Verify: `RcloneMountManager.Core/ViewModels/TypedOptionViewModel.cs`

**Step 1: Write the failing test**

Voeg test toe op commandline output:
- mismatch secret value mag niet in `GetNonDefaultValues`/CLI args terechtkomen

**Step 2: Run test to verify it fails**

Run: `dotnet test --filter FullyQualifiedName~MountOptionsViewModel`
Expected: FAIL totdat mismatch-gating correct doorwerkt.

**Step 3: Write minimal implementation**

Alleen indien nodig: notify/override finetunen zodat gating correct doorwerkt in verzameling van non-default opties.

**Step 4: Run test to verify it passes**

Run: `dotnet test`
Expected: PASS.

**Step 5: Commit**

```bash
git add RcloneMountManager.Core/ViewModels/TypedOptionViewModel.cs RcloneMountManager.Tests
git commit -m "test: verify IsPassword mismatch blocks option inclusion"
```
