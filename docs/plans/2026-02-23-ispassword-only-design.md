# IsPassword-Only Secret UX Design

## Scope

Deze wijziging richt zich alleen op opties die rclone expliciet markeert met `IsPassword=true` in `options/info`.

- Wel: password-masking, oogje (show/hide), verificatieveld voor `IsPassword` opties.
- Niet: extra detectie op helptekst zoals `optional, secret` als `IsPassword` ontbreekt.

## Huidige situatie

- `RcloneOptionsService` leest `IsPassword` al uit rclone JSON.
- De `Text` template toont momenteel voor alle tekstopties een gewone `TextBox`.
- Er is nog geen confirmatieveld en geen inline mismatch-validatie voor secrets.

## Ontwerp

### Gevoelige optie-detectie

Gebruik uitsluitend `IsPassword` uit het model (`IRcloneOptionDefinition.IsPassword`).

### UI-gedrag voor `IsPassword=true`

- Toon input gemaskeerd.
- Toon een oogje om tijdelijk zichtbaar te maken.
- Toon een tweede verificatieveld (ook gemaskeerd, dezelfde zichtbaarheid-toggle).
- Toon inline foutmelding als beide waarden niet matchen.

### Validatie en opslag

- Alleen de primaire waarde wordt doorgegeven aan bestaande opslag/generatieflow.
- Als verificatie niet matcht, geldt de optie als ongeldig voor inclusion (`ShouldInclude=false`).
- Niet-secret opties blijven ongewijzigd.

## Aangeraakte onderdelen

- `RcloneMountManager.Core/ViewModels/TypedOptionViewModel.cs`
  - extra properties/commands voor password visibility en verificatie.
  - match-validatie en include-gedrag voor `IsPassword`.
- `RcloneMountManager.GUI/App.axaml`
  - `Text` DataTemplate conditioneel uitbreiden met password-variant + oogje + confirmatieveld + foutmelding.
- (optioneel) tests in ViewModel testproject voor de nieuwe validatielogica.

## Succescriteria

1. `IsPassword` opties tonen masked + oogje + verificatieveld.
2. Mismatch toont foutmelding en voorkomt inclusion.
3. Match herstelt normale inclusion volgens bestaande default/pin-regels.
4. Niet-`IsPassword` opties gedragen zich exact als voorheen.
