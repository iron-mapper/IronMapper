---
title: Roadmap
description: What the next releases of IronMapper are about.
---

_Updated 2026-10-27. This page is a short summary written by hand; the working plan with every item and its status is
[IRONMAPPER_PLAN.md](../../IRONMAPPER_PLAN.md) (in Russian), and open work is tracked in
[GitHub milestones](https://github.com/iron-mapper/IronMapper/milestones)._

## 2.1.0 — nested objects, constructors, correctness (in progress)

- Nested objects are mapped by the mapping of their type pair, without a `ForMember` per property; a cycle through them
  is warning `IM0004`.
- Incompatible property types and missing pair mappings are error `IM0002` at your code, with a code fix, instead of
  C# errors inside the generated file.
- Destinations without a parameterless constructor — positional records, classes with constructor parameters — are
  created by their constructor; profiles can name a factory (`ConstructWith`) or a parameter without a property
  (`ForCtorParam`).
- Value conversions through pair mappings: `CreateMap<Guid, UserId>().ConvertUsing(UserId.From)` applies to every
  property and parameter of that type, nullable lifted automatically.
- `IM0023`: a nullable value mapped to a non-nullable member is reported at the map instead of `CS8601`/`CS8604` in
  the generated code.
- Still planned for 2.1.0: `null` in, `null` out behind a flag; the size of the generated code; silent data-loss cases
  with `required`/`init` and hooks.

Performance is not measured in 2.1.0: the numbers on this site are from the 2.0.0 run and say so.

## 3.0.0

Breaking changes that were decided but wait for a major version:

- `null` in, `null` out as the default behaviour.
- Unmapped destination properties in profiles (`IM0001`) reported by default.

A rename of the library is planned (the new name is not chosen yet); the packages and this site move with it.

See [Upgrading to 2.1](../../docs/upgrading-to-2.1.md) and the [Changelog](../../CHANGELOG.md) for what has shipped.
