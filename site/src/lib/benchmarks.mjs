// Turns benchmarks/results/summary.json (copied to src/generated/benchmarks.json by scripts/prepare.mjs, which checks
// its schemaVersion) into what the pages and the Open Graph image show. Nothing here holds a number: every value is
// read from the file, and a value that the run did not measure stays null and is shown as "not measured".
//
// Used from Astro components (which import the JSON) and from scripts/og-image.mjs (plain Node), so this module
// takes the parsed summary as an argument instead of importing it.

/** The schema this code understands; prepare.mjs refuses any other version before the build starts. */
export const SUPPORTED_SCHEMA_VERSION = 1;

/** Name of the mapper under test, as the suite names it. */
export const SUBJECT = 'IronMapper';

/** Everything the pages need, derived once from the parsed summary.json. */
export function model(summary) {
  const library = (name) => summary.libraries.find((l) => l.name === name) ?? null;
  return {
    summary,
    baseline: summary.baseline ?? 'Manual',
    /** Label of the run, e.g. "2.0.0": the last segment of `resultsDir` (results/2.0.0). */
    runLabel: (summary.resultsDir ?? '').split('/').filter(Boolean).pop() ?? '',
    /** "2026-10-21" from the run's start stamp "2026-10-21 19:17 UTC". */
    runDate: (summary.run?.startedUtc ?? '').slice(0, 10),
    commit: summary.run?.commit ?? null,
    library,
    subject: library(SUBJECT),
    /** CPU and RAM of the machine (the first two segments of the environment line written by the suite). */
    machineShort: (summary.environment?.machine ?? '').split(' · ').slice(0, 2).join(' · '),
    os: summary.environment?.os ?? null,
    runtime: summary.environment?.runtime ?? null,
    benchmarkDotNet: summary.environment?.benchmarkDotNet ?? null,
    /** Libraries in the order of the report, with the baseline first. */
    libraries: summary.libraries.map((l) => l.name),
    packages: summary.packages ?? [],
  };
}

// ---------------------------------------------------------------------------------------------------------------------
// Formatting (invariant: the same text in both languages; units are symbols)
// ---------------------------------------------------------------------------------------------------------------------

const nf = (digits) => new Intl.NumberFormat('en-US', { minimumFractionDigits: digits, maximumFractionDigits: digits });

export function formatNs(ns) {
  if (ns == null) return null;
  if (ns < 1e3) return `${nf(1).format(ns)} ns`;
  if (ns < 1e6) return `${nf(2).format(ns / 1e3)} µs`;
  if (ns < 1e9) return `${nf(2).format(ns / 1e6)} ms`;
  return `${nf(2).format(ns / 1e9)} s`;
}

/** Decimal units, as the reports use them (1 KB = 1,000 bytes). */
export function formatBytes(bytes, signed = false) {
  if (bytes == null) return null;
  const sign = signed && bytes > 0 ? '+' : bytes < 0 ? '−' : '';
  const b = Math.abs(bytes);
  const text = b < 1e3 ? `${nf(0).format(b)} B` : b < 1e6 ? `${nf(1).format(b / 1e3)} KB` : `${nf(2).format(b / 1e6)} MB`;
  return sign + text;
}

export function formatRatio(ratio) {
  return ratio == null ? null : `×${nf(2).format(ratio)}`;
}

export function formatMs(ms, signed = false) {
  if (ms == null) return null;
  const sign = signed && ms > 0 ? '+' : ms < 0 ? '−' : '';
  return `${sign}${nf(1).format(Math.abs(ms))} ms`;
}

export function formatSeconds(s, signed = false) {
  if (s == null) return null;
  const sign = signed && s > 0 ? '+' : s < 0 ? '−' : '';
  return `${sign}${nf(2).format(Math.abs(s))} s`;
}

// ---------------------------------------------------------------------------------------------------------------------
// Metric tiles of the home page: key → how to read it from the data. project.config.mjs picks the keys.
// Each returns { value, label, note } with text per locale; `value` is null when not measured.
// ---------------------------------------------------------------------------------------------------------------------

const t = (en, ru) => ({ en, ru });

export const metrics = {
  flatRatio: ({ subject }) => {
    const r = subject?.runtime?.['net10.0']?.flat?.ratioToManual ?? null;
    return {
      value: formatRatio(r),
      label: t('hand-written code, flat object', 'от ручного кода, плоский объект'),
      note: t('.NET 10, mean time per object; ×1.00 is the baseline', '.NET 10, среднее время на объект; ×1.00 — ручной код'),
    };
  },
  nestedRatio: ({ subject }) => {
    const r = subject?.runtime?.['net10.0']?.nested?.ratioToManual ?? null;
    return {
      value: formatRatio(r),
      label: t('hand-written code, nested object', 'от ручного кода, вложенный объект'),
      note: t('.NET 10, an order with a customer, an address and 5–20 lines', '.NET 10, заказ с клиентом, адресом и 5–20 строками'),
    };
  },
  coldStartDelta: ({ subject, library }) => {
    const v = subject?.coldStart?.deltaToBaselineMs ?? null;
    const auto = library('AutoMapper')?.coldStart?.deltaToBaselineMs;
    return {
      value: formatMs(v, true),
      label: t('cold start over an app without a mapper', 'холодный старт сверх приложения без маппера'),
      note: auto == null
        ? t('process start plus the first mapping', 'запуск процесса и первый маппинг')
        : t(`process start plus the first mapping; AutoMapper ${formatMs(auto, true)}`, `запуск процесса и первый маппинг; AutoMapper ${formatMs(auto, true)}`),
    };
  },
  nativeAot: ({ subject }) => {
    const works = subject?.nativeAot?.works ?? null;
    return {
      value: works == null ? null : works ? t('yes', 'да') : t('no', 'нет'),
      label: t('NativeAOT: publishes and runs', 'NativeAOT: публикуется и работает'),
      note: t('generated methods and IMapper, zero trim/AOT warnings', 'сгенерированные методы и IMapper, без предупреждений trim/AOT'),
    };
  },
  aotSizeDelta: ({ subject }) => {
    const v = subject?.size?.aotDeltaToBaselineBytes ?? null;
    return {
      value: formatBytes(v, true),
      label: t('to the NativeAOT executable', 'к исполняемому файлу NativeAOT'),
      note: t('against the same console without a mapper', 'относительно той же консоли без маппера'),
    };
  },
  publishSizeDelta: ({ subject }) => {
    const v = subject?.size?.publishDeltaToBaselineBytes ?? null;
    return {
      value: formatBytes(v, true),
      label: t('to a framework-dependent publish', 'к framework-dependent публикации'),
      note: t('one assembly, no NuGet dependencies', 'одна сборка, без зависимостей NuGet'),
    };
  },
  buildTimeDelta: ({ subject }) => {
    const v = subject?.buildTime?.deltaToBaselineSeconds ?? null;
    const n = subject?.buildTime?.pairs;
    return {
      value: formatSeconds(v, true),
      label: t('build time, generator included', 'время сборки с генератором'),
      note: t(`project with ${n ?? '…'} entity/DTO pairs, against the same project without a mapper`, `проект с ${n ?? '…'} парами сущность/DTO, относительно того же проекта без маппера`),
    };
  },
};

/** The tiles picked in project.config.mjs, resolved for one locale. Unknown keys fail the build. */
export function homeMetrics(m0, keys, locale) {
  return keys.map((key) => {
    const def = metrics[key];
    if (!def) throw new Error(`project.config.mjs homeMetrics: unknown metric key "${key}"; known: ${Object.keys(metrics).join(', ')}`);
    const m = def(m0);
    const pick = (x) => (x && typeof x === 'object' && 'en' in x ? x[locale] ?? x.en : x);
    return { key, value: pick(m.value), label: pick(m.label), note: pick(m.note) };
  });
}

// ---------------------------------------------------------------------------------------------------------------------
// Scenario rows for the tables and charts
// ---------------------------------------------------------------------------------------------------------------------

export const scenarios = [
  { key: 'flat', label: t('Flat', 'Плоский объект') },
  { key: 'nested', label: t('Nested', 'Вложенные объекты') },
  { key: 'collection10', label: t('Collection, 10', 'Коллекция, 10') },
  { key: 'collection1000', label: t('Collection, 1 000', 'Коллекция, 1 000') },
  { key: 'collection100000', label: t('Collection, 100 000', 'Коллекция, 100 000') },
  { key: 'customConfig', label: t('Custom config', 'Своя конфигурация') },
  { key: 'inPlace', label: t('In-place', 'In-place') },
  { key: 'diOverheadFlat', label: t('DI overhead, flat', 'Через DI, плоский') },
  { key: 'diOverheadNested', label: t('DI overhead, nested', 'Через DI, вложенные') },
];
