// gridlet.js - the functions a Gridlet component expression can call.
//
// This file is Gridlet's own. It is read-only in the workspace, and it is not a description of the
// built-in functions: it *is* them. The designer imports this module and evaluates every expression
// with the table at the bottom, so what you read here is exactly what `json(data, 2)` does when you
// type it into a property.
//
// It is also the shortest worked example of a module. A component's own behaviour is written the same
// way - a file, plain exports, imported by name - and yours can import this one:
//
//     import { text, json } from './gridlet.js';
//
//     export default class Invoice {
//       #component;
//
//       constructor(component) {
//         this.#component = component;
//       }
//
//       connected() {
//         this.#component.on('row', (row) => {
//           this.#component.field('raw').value = json(row, 2);
//         });
//       }
//     }

// ---- errors ----
// A formula that fails produces an error value, the way a spreadsheet cell does. The value travels:
// anything built on #VALUE! is #VALUE! as well. A property therefore always has something to show,
// and the reason it is wrong is the thing on screen instead of a control that quietly stopped
// moving.
//
// The tag is a plain field rather than a class, because a module that imports this file gets its
// own copy of it and `instanceof` would not hold across the two.

export const ERROR = Object.freeze({
  NAME: '#NAME?',
  VALUE: '#VALUE!',
  DIV0: '#DIV/0!',
  NUM: '#NUM!',
  // Neither of these is a spreadsheet's code. A spreadsheet warns about a circle instead of giving
  // it a value, and it refuses a formula it cannot read rather than accepting one. Gridlet keeps
  // what you typed either way, so both need something to show.
  CIRC: '#CIRC!',
  SYNTAX: '#SYNTAX?',
});

// `detail` is the sentence behind the code - which name was not found, what a module's own function
// threw. The code goes on the component; the detail goes in the property panel, where there is room.
export const error = (code, detail = '') => Object.freeze({ gridletError: true, code, detail });

export const isError = (value) =>
  Boolean(value) && typeof value === 'object' && value.gridletError === true;

/** The first error among some values, or null. Arguments are checked before a function runs. */
export const firstError = (values) => values.find(isError) ?? null;

/** A formula's own answer to a failure: `iferror(data.Total / data.Count, 0)`. */
export const iferror = (value, fallback = '') => (isError(value) ? fallback : value);

/**
 * A value as text. Null and undefined are the absence of a value, not the words "null" and
 * "undefined".
 *
 * Given a format as well, this is a spreadsheet's TEXT: `text(data.Total, '#,##0.00')`,
 * `text(data.Due, 'dd mmm yyyy')`, or one of the named formats a form designer offers, such as
 * `text(data.Due, 'Long Date')`. What it writes follows the component's regional settings - see
 * the section on them below. Anything that is not a string is not a format, so `values.map(text)`
 * still just turns values into text.
 */
export function text(value, pattern) {
  if (isError(value)) return value.code;
  if (typeof pattern === 'string' && pattern !== '') return format(value, pattern, localeOf(this));
  if (value === null || value === undefined) return '';
  return typeof value === 'object' ? json(value) : String(value);
}

/** Anything that is not a number reads as zero, so arithmetic never spreads NaN through a component. */
export function number(value) {
  const parsed = Number(value);
  return Number.isFinite(parsed) ? parsed : 0;
}

/**
 * Data arrives from SQL, where a boolean is often 0/1 or the text "false". Treating those as the
 * false they plainly are keeps `if(data.Active, …)` doing what it reads like.
 */
export function truthy(value) {
  if (typeof value === 'string') {
    const trimmed = value.trim().toLowerCase();
    return !(trimmed === '' || trimmed === 'false' || trimmed === '0');
  }
  return Boolean(value);
}

/** A row, or a whole result, as text. `json(data, 2)` is the readable component. */
export function json(value, indent = 0) {
  try {
    return JSON.stringify(value, null, number(indent)) ?? '';
  } catch {
    return String(value);
  }
}

export const min = (...values) => Math.min(...values.map(number));
export const max = (...values) => Math.max(...values.map(number));
export const sum = (...values) => values.reduce((total, value) => total + number(value), 0);
export const average = (...values) => (values.length ? sum(...values) / values.length : 0);
export const count = (...values) =>
  values.filter((value) => value !== null && value !== undefined && value !== '').length;
export const floor = (value) => Math.floor(number(value));
export const ceil = (value) => Math.ceil(number(value));
export const abs = (value) => Math.abs(number(value));

export function round(value, places = 0) {
  const factor = 10 ** number(places);
  return Math.round(number(value) * factor) / factor;
}

/** Written `if(test, then, otherwise)` in an expression; `if` is not a name a function can have. */
export const choose = (condition, then, otherwise = '') => truthy(condition) ? then : otherwise;

/** The first value that is actually there. Empty text counts as missing. */
export const coalesce = (...values) =>
  values.find((value) => value !== null && value !== undefined && value !== '') ?? '';

export const concat = (...values) => values.map((value) => text(value)).join('');
export const upper = (value) => text(value).toUpperCase();
export const lower = (value) => text(value).toLowerCase();
export const trim = (value) => text(value).trim();
export const len = (value) => text(value).length;

// ---- regional settings ----
// Numbers and dates are written for whoever reads them. A component says which locale that is, or
// where to take one from, and may override the separators and its short date and time patterns -
// the same choices Windows' regional settings offer, and the ones Access and Excel format with.
// Month and day names, AM and PM, and the order a date's parts come in are the browser's own data,
// read through Intl, so nothing here is a table of languages that could fall behind.
//
// Where the locale comes from:
//
//   ''         Inherit: the language of the page the component is on - the `lang` it declares.
//              Gridlet's own public page declares the server's language.
//   'browser'  The reader's own browser language.
//   'server'   The culture the server formats with, for the request that served the page.
//   'en-GB'    That locale, whoever is reading.
//
// A locale the browser has no data for falls back to the browser's own language, so a component
// always formats as something.
//
// A built-in function that formats or reads dates is handed the locale of the component it runs in as
// `this`: `{ locale }`, where the locale is what `localeFor` returns. Called from a module, with no
// `this`, it uses the page's language.

const DAY = 86400000;
const HOUR = 3600000;
const MINUTE = 60000;
const SECOND = 1000;

const supportedLocale = (tag) => {
  const written = String(tag ?? '').trim();
  if (!written) return '';
  try {
    return Intl.DateTimeFormat.supportedLocalesOf([written])[0] || '';
  } catch {
    // Not a locale at all: `en_GB`, `english`, a typing mistake.
    return '';
  }
};

// A day on which every field of a date is told apart from every other, and where a leading zero
// shows: the 2nd of January 2026, a Friday, at 03:04:05.
const sampleMoment = () => new Date(2026, 0, 2, 3, 4, 5);

// A date format's words for a piece of text that is not a code. Punctuation and spaces are written as
// they are; anything with a letter in it is quoted, so `de` in `2 de enero de 2026` is not a day.
const formatLiteral = (value) =>
  /^[^\p{L}\d"\\]*$/u.test(value) ? value : `"${value.replace(/"/g, '')}"`;

// The names a date is written with, in the form a date uses. Polish writes the 2nd of January as
// `2 stycznia` and the month on its own as `styczeń`; asking for the month beside a day is what
// gets the first.
const monthNames = (tag, width) => Array.from({ length: 12 }, (_, month) =>
  new Intl.DateTimeFormat(tag, { day: 'numeric', month: width })
    .formatToParts(new Date(2026, month, 15))
    .find((part) => part.type === 'month')?.value ?? '');

// Sunday first, as a spreadsheet's WEEKDAY counts: the 4th of January 2026 is a Sunday.
const dayNames = (tag, width) => Array.from({ length: 7 }, (_, day) =>
  new Intl.DateTimeFormat(tag, { weekday: width }).format(new Date(2026, 0, 4 + day)));

const dayPeriod = (tag, hour) =>
  new Intl.DateTimeFormat(tag, { hour: 'numeric', hour12: true })
    .formatToParts(new Date(2026, 0, 2, hour))
    .find((part) => part.type === 'dayPeriod')?.value ?? (hour < 12 ? 'AM' : 'PM');

// How a locale writes a date or a time, as the format codes this file reads. Asking Intl for the
// parts of a known date says which field came where and whether it was padded, which is everything
// a pattern needs.
function localePattern(tag, options, names) {
  return new Intl.DateTimeFormat(tag, { ...options, numberingSystem: 'latn' })
    .formatToParts(sampleMoment())
    .map(({ type, value }) => {
      switch (type) {
        case 'year': return value.length <= 2 ? 'yy' : 'yyyy';
        case 'month':
          if (/^\d+$/.test(value)) return value.length > 1 ? 'mm' : 'm';
          return value === names.months[0] ? 'mmmm' : 'mmm';
        case 'day': return value.length > 1 ? 'dd' : 'd';
        case 'weekday': return value === names.days[5] ? 'dddd' : 'ddd';
        case 'hour': return value.length > 1 ? 'hh' : 'h';
        case 'minute': return 'mm';
        case 'second': return 'ss';
        case 'dayPeriod': return 'AM/PM';
        default: return formatLiteral(value);
      }
    })
    .join('');
}

// The order a short date's day, month and year are typed in, read off its pattern: `dd/mm/yyyy` is
// day, month, year.
function fieldOrder(pattern) {
  const order = [];
  for (const token of scanFormat(pattern)) {
    const field = token.code?.toLowerCase();
    if ((field === 'd' || field === 'm' || field === 'y') && !order.includes(field)) order.push(field);
  }
  return ['d', 'm', 'y'].every((field) => order.includes(field)) ? order : ['m', 'd', 'y'];
}

function buildLocale(tag, overrides) {
  const names = {
    months: monthNames(tag, 'long'),
    monthsShort: monthNames(tag, 'short'),
    days: dayNames(tag, 'long'),
    daysShort: dayNames(tag, 'short'),
  };
  const numberParts = new Intl.NumberFormat(tag, { numberingSystem: 'latn' }).formatToParts(1234567.5);
  const numberPart = (type, fallback) =>
    numberParts.find((part) => part.type === type)?.value ?? fallback;

  // A four-digit year, as a form designer's Short Date writes it, rather than the two digits a
  // browser's own short style drops to in some locales.
  const shortDate = overrides.shortDate
    || localePattern(tag, { year: 'numeric', month: 'numeric', day: 'numeric' }, names);

  // A decimal separator chosen to be the locale's thousands separator takes the locale's decimal
  // separator as its thousands, so 1,234.50 in en-GB with a decimal comma is 1.234,50 rather than a
  // 1,234,50 that nothing could read back.
  const decimal = overrides.decimal || numberPart('decimal', '.');
  const localeThousands = numberPart('group', ',');
  const thousands = overrides.thousands
    || (localeThousands === decimal ? numberPart('decimal', '.') : localeThousands);

  // A clock that runs to 23 writes its hour with two digits, as 14:05 and 03:05; one that runs to 12
  // writes 3:05 PM.
  const cycle = new Intl.DateTimeFormat(tag, { hour: 'numeric' }).resolvedOptions().hourCycle;
  const hour = cycle === 'h23' || cycle === 'h24' ? '2-digit' : 'numeric';

  return Object.freeze({
    tag,
    months: Object.freeze(names.months),
    monthsShort: Object.freeze(names.monthsShort),
    days: Object.freeze(names.days),
    daysShort: Object.freeze(names.daysShort),
    am: dayPeriod(tag, 3),
    pm: dayPeriod(tag, 15),
    decimal,
    thousands,
    shortDate,
    mediumDate: localePattern(tag, { year: 'numeric', month: 'short', day: 'numeric' }, names),
    longDate: localePattern(tag, { weekday: 'long', year: 'numeric', month: 'long', day: 'numeric' }, names),
    shortTime: overrides.shortTime || localePattern(tag, { hour, minute: '2-digit' }, names),
    mediumTime: localePattern(tag, { hour: 'numeric', minute: '2-digit', hour12: true }, names),
    longTime: localePattern(tag, { hour, minute: '2-digit', second: '2-digit' }, names),
    order: Object.freeze(fieldOrder(shortDate)),
  });
}

const locales = new Map();

/**
 * The locale a component formats with.
 *
 * `settings` are the component's own: `locale` (see above), and `decimalSeparator`,
 * `thousandsSeparator`, `dateFormat` and `timeFormat`, each of which replaces what the locale would
 * use when it is not empty. `environment` says where the places a locale can come from stand:
 * `language` for the page, `server` for the server, `browser` for the reader. What it leaves out is
 * read from the page this file is running in.
 */
export function localeFor(settings = {}, environment = {}) {
  const source = String(settings?.locale ?? '').trim();
  const browser = environment.browser ?? globalThis.navigator?.language;
  const page = environment.language ?? globalThis.document?.documentElement?.lang;

  let wanted;
  switch (source.toLowerCase()) {
    case '':
    case 'inherit': wanted = page; break;
    case 'browser': wanted = browser; break;
    case 'server': wanted = environment.server; break;
    default: wanted = source;
  }
  const tag = [wanted, browser].map(supportedLocale).find(Boolean) || 'en-US';

  const overrides = {
    decimal: String(settings?.decimalSeparator ?? ''),
    thousands: String(settings?.thousandsSeparator ?? ''),
    shortDate: String(settings?.dateFormat ?? '').trim(),
    shortTime: String(settings?.timeFormat ?? '').trim(),
  };
  const key = JSON.stringify([tag, overrides]);
  if (!locales.has(key)) {
    // Every keystroke in an override box is a locale of its own, so the cache is emptied rather than
    // allowed to grow all day.
    if (locales.size >= 50) locales.clear();
    locales.set(key, buildLocale(tag, overrides));
  }
  return locales.get(key);
}

const localeOf = (context) => context?.locale ?? localeFor();

// ---- dates and times ----
// A date travels between formulas as ISO text: `2026-12-31` for a date, `2026-12-31T14:30:00` for a
// date and time, `14:30:00` for a time. That is how a date arrives from SQL, it is text every
// function here already handles, two of them compare in the order they happen, and it means the
// same thing whatever language the reader has. Formatting it for a person is the last step, with
// `text`.
//
// A date and time with no zone is the time on the clock where it was recorded and is kept as it is.
// One with a zone - `Z`, `+02:00` - names a moment, and is read as the reader's own clock time.
//
// A time on its own stands on the 30th of December 1899, where Access and Excel stand one, so it
// has a date to format when a format asks for one.

const pad = (value, width) => String(value).padStart(width, '0');

function utc(year, month, day, hour = 0, minute = 0, second = 0, millisecond = 0) {
  // `Date.UTC` reads a year below 100 as 1900 and something; setting the year does not.
  const moment = new Date(0);
  moment.setUTCFullYear(year, month - 1, day);
  moment.setUTCHours(hour, minute, second, millisecond);
  return moment.getTime();
}

const momentOf = (parts) => utc(parts.y, parts.mo, parts.d, parts.h, parts.mi, parts.s, parts.ms);

function partsAt(moment, kind) {
  const at = new Date(moment);
  const parts = {
    kind,
    y: at.getUTCFullYear(),
    mo: at.getUTCMonth() + 1,
    d: at.getUTCDate(),
    h: at.getUTCHours(),
    mi: at.getUTCMinutes(),
    s: at.getUTCSeconds(),
    ms: at.getUTCMilliseconds(),
  };
  return kind === 'date' ? { ...parts, h: 0, mi: 0, s: 0, ms: 0 } : parts;
}

const localParts = (moment, kind) => ({
  kind,
  y: moment.getFullYear(),
  mo: moment.getMonth() + 1,
  d: moment.getDate(),
  h: kind === 'date' ? 0 : moment.getHours(),
  mi: kind === 'date' ? 0 : moment.getMinutes(),
  s: kind === 'date' ? 0 : moment.getSeconds(),
  ms: kind === 'date' ? 0 : moment.getMilliseconds(),
});

const daysIn = (year, month) => new Date(utc(year, month + 1, 0)).getUTCDate();

// The parts, if they are a real date and time, or null.
function checkedParts(parts) {
  const valid = Number.isInteger(parts.y) && parts.y >= 1 && parts.y <= 9999
    && parts.mo >= 1 && parts.mo <= 12
    && parts.d >= 1 && parts.d <= daysIn(parts.y, parts.mo)
    && parts.h >= 0 && parts.h <= 23 && parts.mi >= 0 && parts.mi <= 59
    && parts.s >= 0 && parts.s <= 59;
  return valid ? parts : null;
}

const TIME_DATE = { y: 1899, mo: 12, d: 30 };

const ISO_DATE = /^(\d{4})-(\d{2})-(\d{2})$/;
const ISO_DATE_TIME =
  /^(\d{4})-(\d{2})-(\d{2})[T ](\d{2}):(\d{2})(?::(\d{2})(?:[.,](\d+))?)?\s*(Z|[+-]\d{2}(?::?\d{2})?)?$/i;
const ISO_TIME = /^(\d{1,2}):(\d{2})(?::(\d{2})(?:[.,](\d+))?)?$/;

const milliseconds = (digits) => (digits ? Number(digits.slice(0, 3).padEnd(3, '0')) : 0);

function isoParts(written) {
  let match = ISO_DATE.exec(written);
  if (match) {
    return checkedParts({ kind: 'date', y: +match[1], mo: +match[2], d: +match[3], h: 0, mi: 0, s: 0, ms: 0 });
  }
  match = ISO_DATE_TIME.exec(written);
  if (match) {
    const parts = checkedParts({
      kind: 'datetime', y: +match[1], mo: +match[2], d: +match[3],
      h: +match[4], mi: +match[5], s: +(match[6] ?? 0), ms: milliseconds(match[7]),
    });
    if (!parts || !match[8]) return parts;
    // A zone names a moment. The offset is taken off by hand, because what `new Date` accepts as a
    // zone is narrower than what the pattern lets through, and a date it does not like it corrects
    // rather than refuses.
    const zone = /^([+-])(\d{2}):?(\d{2})?$/.exec(match[8]);
    if (zone && (Number(zone[2]) > 23 || Number(zone[3] ?? 0) > 59)) return null;
    const offset = zone ? (zone[1] === '-' ? -1 : 1) * (Number(zone[2]) * 60 + Number(zone[3] ?? 0)) : 0;
    return checkedParts(localParts(new Date(momentOf(parts) - offset * MINUTE), 'datetime'));
  }
  match = ISO_TIME.exec(written);
  if (match) {
    return checkedParts({
      kind: 'time', ...TIME_DATE, h: +match[1], mi: +match[2], s: +(match[3] ?? 0), ms: milliseconds(match[4]),
    });
  }
  return null;
}

const escapeRegExp = (value) => value.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');

// Which month a word names, in the locale's own words: its full name, its short one, or the start of
// either. The start is what lets `grudzień` find December even though a Polish date writes it
// `grudnia`, and what lets `Sept.` find September.
function monthNamed(word, locale) {
  const wanted = word.replace(/\.$/, '').toLocaleLowerCase(locale.tag);
  if (wanted.length < 3) return 0;
  for (let month = 0; month < 12; month += 1) {
    const long = locale.months[month].toLocaleLowerCase(locale.tag);
    const short = locale.monthsShort[month].replace(/\.$/, '').toLocaleLowerCase(locale.tag);
    if (long === wanted || short === wanted || long.startsWith(wanted)
      || (short.length >= 3 && wanted.startsWith(short))) return month + 1;
  }
  return 0;
}

const isDayName = (word, locale) => {
  const wanted = word.replace(/\.$/, '').toLocaleLowerCase(locale.tag);
  return [...locale.days, ...locale.daysShort].some((name) =>
    name.replace(/\.$/, '').toLocaleLowerCase(locale.tag) === wanted);
};

// A date as a person types it, in their locale: `31/12/2026` in en-GB, `12/31/2026` in en-US,
// `31.12.2026 14:30` in pl-PL, `31 Dec 2026`, `December 31, 2026 2:30 PM`. A year written first is a
// year whatever the locale. A two-digit year is 2000 to 2029 below 30 and 1930 to 1999 from it, as a
// spreadsheet reads one; no year at all is this year.
function localeParts(written, locale) {
  const periods = [locale.am, locale.pm, 'am', 'pm', 'a.m.', 'p.m.', 'a', 'p']
    .filter(Boolean).map(escapeRegExp).join('|');
  const clock = new RegExp(
    `(\\d{1,2})\\s*:\\s*(\\d{2})(?:\\s*:\\s*(\\d{2})(?:[.,](\\d+))?)?\\s*(${periods})?(?!\\p{L})`, 'iu');

  let dateText = written;
  let clockTime = null;
  const match = clock.exec(written);
  if (match) {
    let hour = Number(match[1]);
    const marker = match[5]?.toLocaleLowerCase(locale.tag);
    if (marker) {
      if (hour < 1 || hour > 12) return null;
      const pm = marker === locale.pm.toLocaleLowerCase(locale.tag)
        || (marker !== locale.am.toLocaleLowerCase(locale.tag) && marker.startsWith('p'));
      hour = (hour % 12) + (pm ? 12 : 0);
    }
    clockTime = { h: hour, mi: Number(match[2]), s: Number(match[3] ?? 0), ms: milliseconds(match[4]) };
    dateText = `${written.slice(0, match.index)} ${written.slice(match.index + match[0].length)}`;
  }

  const words = dateText.match(/\p{L}+\.?|\d+/gu) ?? [];
  // Whatever is left between the words has to be the kind of thing that separates a date's parts.
  if (dateText.replace(/\p{L}+\.?|\d+/gu, '').replace(/[\s,./\-]/g, '')) return null;
  if (!words.length) return clockTime ? checkedParts({ kind: 'time', ...TIME_DATE, ...clockTime }) : null;

  const numbers = [];
  let month = 0;
  for (const word of words) {
    if (/^\d+$/.test(word)) {
      numbers.push(word);
      continue;
    }
    const named = month ? 0 : monthNamed(word, locale);
    if (named) month = named;
    else if (!isDayName(word, locale)) return null;
  }

  const fields = month ? locale.order.filter((field) => field !== 'm') : [...locale.order];
  // A number of three or more digits is the year wherever it stands, and the rest keep the locale's
  // order - `December 31, 2026` in a locale that writes its dates year first. With no month named, a
  // year written first is followed by the month, whatever the locale.
  const yearAt = numbers.findIndex((written) => written.length >= 3);
  let slots;
  if (yearAt >= 0) {
    const others = yearAt === 0 && !month ? ['m', 'd'] : fields.filter((field) => field !== 'y');
    if (numbers.length !== others.length + 1) return null;
    slots = [...others.slice(0, yearAt), 'y', ...others.slice(yearAt)];
  } else if (numbers.length === fields.length) slots = fields;
  else if (numbers.length === fields.length - 1) slots = fields.filter((field) => field !== 'y');
  else return null;
  if (numbers.length !== slots.length) return null;

  const found = { y: new Date().getFullYear(), m: month, d: 0 };
  slots.forEach((slot, index) => {
    const value = Number(numbers[index]);
    found[slot] = slot === 'y' && numbers[index].length <= 2 ? value + (value < 30 ? 2000 : 1900) : value;
  });

  return checkedParts({
    kind: clockTime ? 'datetime' : 'date', y: found.y, mo: found.m, d: found.d,
    h: 0, mi: 0, s: 0, ms: 0, ...clockTime,
  });
}

/**
 * A date, a time, or both, as its parts - `{ kind, y, mo, d, h, mi, s, ms }` - or null when the value
 * is none of those. ISO text is read first, whatever the locale; then text as a person in `locale`
 * types a date. A JavaScript Date is read as the reader's own clock time.
 */
export function dateParts(value, locale = localeFor()) {
  if (value instanceof Date) return Number.isNaN(value.getTime()) ? null : localParts(value, 'datetime');
  if (typeof value !== 'string') return null;
  const written = value.trim();
  if (!written) return null;
  return isoParts(written) ?? localeParts(written, locale);
}

/** The parts of a date as the ISO text a formula passes it on as. */
export function isoText(parts) {
  const date = `${pad(parts.y, 4)}-${pad(parts.mo, 2)}-${pad(parts.d, 2)}`;
  const time = `${pad(parts.h, 2)}:${pad(parts.mi, 2)}:${pad(parts.s, 2)}${parts.ms ? `.${pad(parts.ms, 3)}` : ''}`;
  if (parts.kind === 'date') return date;
  return parts.kind === 'time' ? time : `${date}T${time}`;
}

// An answer, or #NUM! for a date no calendar here can write.
function dateResult(parts) {
  if (!parts || !Number.isInteger(parts.y) || parts.y < 1 || parts.y > 9999) {
    return error(ERROR.NUM, 'The date is outside the years 1 to 9999.');
  }
  return isoText(parts);
}

function dateArgument(value, context) {
  return dateParts(value, localeOf(context))
    ?? error(ERROR.VALUE, `"${text(value)}" is not a date or a time.`);
}

function addMonths(parts, months) {
  const index = parts.y * 12 + (parts.mo - 1) + months;
  const y = Math.floor(index / 12);
  const mo = index - y * 12 + 1;
  return { ...parts, y, mo, d: Math.min(parts.d, daysIn(y, mo)) };
}

/** Today's date, on the reader's clock. */
export const today = () => isoText(localParts(new Date(), 'date'));

/** The date and time now, to the second, on the reader's clock. */
export const now = () => isoText({ ...localParts(new Date(), 'datetime'), ms: 0 });

/**
 * A date from its parts. A month or day past the end carries on into the next, as DATE does. A
 * two-digit year is read the way DateSerial reads one: 0 to 29 is 2000 to 2029, 30 to 99 is 1930 to
 * 1999 - the same window `datevalue` reads a typed one with.
 */
export function date(year, month, day) {
  let whole = Math.trunc(number(year));
  if (whole >= 0 && whole < 100) whole += whole < 30 ? 2000 : 1900;
  const moment = utc(whole, Math.trunc(number(month)), Math.trunc(number(day)));
  return Number.isFinite(moment) ? dateResult(partsAt(moment, 'date')) : error(ERROR.NUM, 'That is not a date.');
}

/** A time of day from its parts. Past midnight wraps round, as TIME does. */
export function time(hour, minute = 0, second = 0) {
  const total = Math.trunc(number(hour)) * 3600 + Math.trunc(number(minute)) * 60 + Math.trunc(number(second));
  if (total < 0) return error(ERROR.NUM, 'A time of day cannot be negative.');
  return isoText(partsAt(utc(TIME_DATE.y, TIME_DATE.mo, TIME_DATE.d) + (total % 86400) * SECOND, 'time'));
}

/** The date in a piece of text, typed the way the component's locale types one. */
export function datevalue(value) {
  const parts = dateArgument(value, this);
  if (isError(parts)) return parts;
  if (parts.kind === 'time') return error(ERROR.VALUE, `"${text(value)}" has no date in it.`);
  return isoText({ ...parts, kind: 'date' });
}

/** The time of day in a piece of text, or in a date and time. A date on its own is midnight. */
export function timevalue(value) {
  const parts = dateArgument(value, this);
  return isError(parts) ? parts : isoText({ ...parts, kind: 'time' });
}

const datePart = (field) => function part(value) {
  const parts = dateArgument(value, this);
  return isError(parts) ? parts : parts[field];
};

export const year = datePart('y');
export const month = datePart('mo');
export const day = datePart('d');
export const hour = datePart('h');
export const minute = datePart('mi');
export const second = datePart('s');

/** The day of the week, counted as WEEKDAY counts it: 1 is Sunday, or with 2, 1 is Monday, or with 3, 0 is Monday. */
export function weekday(value, type = 1) {
  const parts = dateArgument(value, this);
  if (isError(parts)) return parts;
  const fromSunday = new Date(utc(parts.y, parts.mo, parts.d)).getUTCDay();
  switch (Math.trunc(number(type))) {
    case 1: return fromSunday + 1;
    case 2: return ((fromSunday + 6) % 7) + 1;
    case 3: return (fromSunday + 6) % 7;
    default: return error(ERROR.NUM, 'weekday counts with 1, 2 or 3.');
  }
}

/** The same day some months later, or earlier. The 31st of January and one month is the 28th of February. */
export function edate(value, months) {
  const parts = dateArgument(value, this);
  if (isError(parts)) return parts;
  return dateResult({ ...addMonths(parts, Math.trunc(number(months))), kind: 'date' });
}

/** The last day of the month some months later, or earlier. */
export function eomonth(value, months) {
  const parts = dateArgument(value, this);
  if (isError(parts)) return parts;
  const moved = addMonths(parts, Math.trunc(number(months)));
  return dateResult({ ...moved, kind: 'date', d: daysIn(moved.y, moved.mo) });
}

const INTERVALS = 'yyyy, q, m, y, d, w, ww, h, n or s';

/**
 * A date moved by some number of intervals, as DateAdd moves one: `dateadd('m', 1, data.Due)`.
 * The intervals are years `yyyy`, quarters `q`, months `m`, days `d` (or `y` or `w`), weeks `ww`,
 * hours `h`, minutes `n` and seconds `s`. A date moved by hours becomes a date and time.
 */
export function dateadd(interval, amount, value) {
  const parts = dateArgument(value, this);
  if (isError(parts)) return parts;
  const by = Math.trunc(number(amount));
  const shift = (step, timed) => {
    const kind = parts.kind === 'date' && timed ? 'datetime' : parts.kind;
    const moved = partsAt(momentOf(parts) + by * step, kind);
    // A time on its own stays a time of day, and a time of day wraps round.
    return kind === 'time' ? { ...moved, ...TIME_DATE } : moved;
  };
  switch (String(interval ?? '').trim().toLowerCase()) {
    case 'yyyy': return dateResult(addMonths(parts, by * 12));
    case 'q': return dateResult(addMonths(parts, by * 3));
    case 'm': return dateResult(addMonths(parts, by));
    case 'y':
    case 'd':
    case 'w': return dateResult(shift(DAY, false));
    case 'ww': return dateResult(shift(7 * DAY, false));
    case 'h': return dateResult(shift(HOUR, true));
    case 'n': return dateResult(shift(MINUTE, true));
    case 's': return dateResult(shift(SECOND, true));
    default: return error(ERROR.VALUE, `"${text(interval)}" is not an interval. Use ${INTERVALS}.`);
  }
}

/**
 * How many interval boundaries lie between two dates, as DateDiff counts them:
 * `datediff('d', data.Ordered, data.Shipped)`. It counts the boundaries crossed rather than whole
 * intervals, so 23:00 to 01:00 the next day is one day, and December to January is one year. Weeks
 * `ww` start on a Sunday; `w` is whole sevens of days.
 */
export function datediff(interval, first, last) {
  const from = dateArgument(first, this);
  if (isError(from)) return from;
  const to = dateArgument(last, this);
  if (isError(to)) return to;
  const dayNumber = (parts) => Math.floor(utc(parts.y, parts.mo, parts.d) / DAY);
  const across = (step) => Math.floor(momentOf(to) / step) - Math.floor(momentOf(from) / step);
  switch (String(interval ?? '').trim().toLowerCase()) {
    case 'yyyy': return to.y - from.y;
    case 'q': return (to.y * 4 + Math.floor((to.mo - 1) / 3)) - (from.y * 4 + Math.floor((from.mo - 1) / 3));
    case 'm': return (to.y * 12 + to.mo) - (from.y * 12 + from.mo);
    case 'y':
    case 'd': return dayNumber(to) - dayNumber(from);
    case 'w': return Math.trunc((dayNumber(to) - dayNumber(from)) / 7);
    // The 1st of January 1970 was a Thursday, four days after a Sunday.
    case 'ww': return Math.floor((dayNumber(to) + 4) / 7) - Math.floor((dayNumber(from) + 4) / 7);
    case 'h': return across(HOUR);
    case 'n': return across(MINUTE);
    case 's': return across(SECOND);
    default: return error(ERROR.VALUE, `"${text(interval)}" is not an interval. Use ${INTERVALS}.`);
  }
}

// ---- numbers as people write them ----

// One way to split a run of digits, so a long line of them that fails at its last character fails at
// once rather than after trying every place the point could have been.
const PLAIN_NUMBER = /^[-+]?(?:\d+(?:\.\d*)?|\.\d+)(?:e[-+]?\d+)?$/i;

// Nobody writes a number longer than this, and text that long is not read as one.
const LONGEST_NUMBER = 400;

/**
 * A number as a person writes one in `locale`: `1 234,5` in pl-PL, `(1,234.50)` in en-US, `12%`.
 * Spaces are ignored, the thousands separator is dropped, brackets or a trailing minus make it
 * negative, and each `%` divides by a hundred. Null when it is not a number.
 */
export function parseNumber(value, locale = localeFor(), decimal = locale.decimal, thousands = locale.thousands) {
  if (typeof value === 'number') return Number.isFinite(value) ? value : null;
  let written = String(value ?? '').replace(/\s/g, '');
  if (!written || written.length > LONGEST_NUMBER) return null;

  let negative = false;
  if (/^\(.*\)$/.test(written)) {
    negative = true;
    written = written.slice(1, -1);
  }
  let percent = 0;
  while (written.endsWith('%')) {
    percent += 1;
    written = written.slice(0, -1);
  }
  if (/^[^-+].*-$/.test(written)) {
    negative = !negative;
    written = written.slice(0, -1);
  }

  const point = decimal ? written.indexOf(decimal) : -1;
  let whole = point < 0 ? written : written.slice(0, point);
  const fraction = point < 0 ? '' : written.slice(point + decimal.length);
  if (thousands && !/^\s+$/.test(thousands)) {
    if (fraction.includes(thousands)) return null;
    whole = whole.split(thousands).join('');
  }
  const plain = point < 0 ? whole : `${whole}.${fraction}`;
  if (!PLAIN_NUMBER.test(plain)) return null;
  const result = Number(plain) / 100 ** percent;
  if (!Number.isFinite(result)) return null;
  return negative ? -result : result;
}

/**
 * The number in a piece of text, as NUMBERVALUE reads it: with the component's separators, or with
 * the ones given. Empty text is zero.
 */
export function numbervalue(value, decimalSeparator, thousandsSeparator) {
  if (value === null || value === undefined || value === '') return 0;
  const locale = localeOf(this);
  const found = parseNumber(value, locale,
    decimalSeparator === undefined ? locale.decimal : text(decimalSeparator),
    thousandsSeparator === undefined ? locale.thousands : text(thousandsSeparator));
  return found === null ? error(ERROR.VALUE, `"${text(value)}" is not a number.`) : found;
}

// ---- formats ----
// The codes a format is written in are a spreadsheet's, because those are the ones people already
// know: `#,##0.00`, `0%`, `0.00E+00`, `dd/mm/yyyy`, `h:mm AM/PM`, `"Due "d mmm`. Quoted text and a
// backslash write themselves; brackets - a colour, a condition - are skipped; `;` separates what a
// positive number, a negative one, zero and text are written with. The named formats are the ones
// Access offers in a text box's Format.
//
// The separators and names they write are the locale's: `#,##0.00` writes 1234.5 as `1,234.50` in
// en-GB and `1 234,50` in pl-PL, and `mmmm` is `December` or `grudnia`.

const NAMED_FORMATS = new Map([
  ['general', { general: 'number' }],
  ['generalnumber', { general: 'number' }],
  ['fixed', { pattern: '0.00' }],
  ['standard', { pattern: '#,##0.00' }],
  ['percent', { pattern: '0.00%' }],
  ['scientific', { pattern: '0.00E+00' }],
  ['generaldate', { general: 'date' }],
  ['longdate', { locale: 'longDate' }],
  ['mediumdate', { locale: 'mediumDate' }],
  ['shortdate', { locale: 'shortDate' }],
  ['longtime', { locale: 'longTime' }],
  ['mediumtime', { locale: 'mediumTime' }],
  ['shorttime', { locale: 'shortTime' }],
]);

/** The named formats, as they are written in a Format box. */
export const FORMAT_NAMES = Object.freeze([
  'General Number', 'Fixed', 'Standard', 'Percent', 'Scientific',
  'General Date', 'Long Date', 'Medium Date', 'Short Date', 'Long Time', 'Medium Time', 'Short Time',
]);

function scanFormat(pattern) {
  const tokens = [];
  const source = String(pattern ?? '');
  for (let at = 0; at < source.length;) {
    const character = source[at];
    if (character === '"') {
      const end = source.indexOf('"', at + 1);
      const stop = end < 0 ? source.length : end;
      tokens.push({ literal: source.slice(at + 1, stop) });
      at = stop + 1;
    } else if (character === '\\') {
      tokens.push({ literal: source[at + 1] ?? '' });
      at += 2;
    } else if (character === '[') {
      // A colour or a condition says nothing about the characters. `[h]`, `[m]` and `[s]` are a
      // spreadsheet's elapsed hours, minutes and seconds, and write the field they name.
      const end = source.indexOf(']', at);
      const stop = end < 0 ? source.length : end;
      const inside = source.slice(at + 1, stop);
      if (/^(?:h+|m+|s+)$/i.test(inside)) for (const code of inside) tokens.push({ code });
      at = stop + 1;
    } else if (character === '_') {
      // Room for a character, which in text is a space.
      tokens.push({ literal: ' ' });
      at += 2;
    } else if (character === '*') {
      // Repeat a character to fill the cell. A value here has no cell to fill.
      at += 2;
    } else {
      tokens.push({ code: character });
      at += 1;
    }
  }
  return tokens;
}

function sectionsOf(tokens) {
  const sections = [[]];
  for (const token of tokens) {
    if (token.code === ';') sections.push([]);
    else sections.at(-1).push(token);
  }
  return sections;
}

const isDateFormat = (tokens) => tokens.some((token) => /^[ydhms]$/i.test(token.code ?? ''));

function formatDate(parts, tokens, locale) {
  const items = [];
  for (let at = 0; at < tokens.length; at += 1) {
    const token = tokens[at];
    if (token.literal !== undefined) {
      items.push(token);
      continue;
    }
    const ahead = tokens.slice(at, at + 5).map((next) => next.code ?? '\u0000').join('');
    if (/^am\/pm$/i.test(ahead)) {
      items.push({ period: 'full' });
      at += 4;
      continue;
    }
    if (/^a\/p$/i.test(ahead.slice(0, 3))) {
      items.push({ period: 'letter', upper: token.code === 'A' });
      at += 2;
      continue;
    }
    const field = token.code.toLowerCase();
    if ('ydhms'.includes(field)) {
      let size = 1;
      while (tokens[at + 1]?.code?.toLowerCase() === field) {
        size += 1;
        at += 1;
      }
      items.push({ field, size });
      continue;
    }
    if (token.code === '.' && items.at(-1)?.field === 's' && tokens[at + 1]?.code === '0') {
      let size = 0;
      while (tokens[at + 1]?.code === '0') {
        size += 1;
        at += 1;
      }
      items.push({ fraction: size });
      continue;
    }
    items.push({ literal: token.code });
  }

  // An `m` beside an hour or a second is minutes, the way a spreadsheet reads `h:mm` and `mm:ss`.
  const fields = items.filter((item) => item.field);
  fields.forEach((item, index) => {
    if (item.field === 'm' && (fields[index - 1]?.field === 'h' || fields[index + 1]?.field === 's')) {
      item.field = 'n';
    }
  });

  const twelveHour = items.some((item) => item.period);
  const afternoon = parts.h >= 12;
  const weekdayIndex = new Date(utc(parts.y, parts.mo, parts.d)).getUTCDay();
  const digits = (value, size) => (size >= 2 ? pad(value, 2) : String(value));

  return items.map((item) => {
    if (item.literal !== undefined) return item.literal;
    if (item.period === 'full') return afternoon ? locale.pm : locale.am;
    if (item.period === 'letter') {
      const letter = afternoon ? 'P' : 'A';
      return item.upper ? letter : letter.toLowerCase();
    }
    if (item.fraction !== undefined) {
      return locale.decimal + pad(parts.ms, 3).slice(0, item.fraction).padEnd(item.fraction, '0');
    }
    switch (item.field) {
      case 'y': return item.size <= 2 ? pad(parts.y % 100, 2) : pad(parts.y, 4);
      case 'm':
        if (item.size <= 2) return digits(parts.mo, item.size);
        if (item.size === 3) return locale.monthsShort[parts.mo - 1];
        return item.size === 4 ? locale.months[parts.mo - 1] : locale.months[parts.mo - 1].charAt(0);
      case 'd':
        if (item.size <= 2) return digits(parts.d, item.size);
        return item.size === 3 ? locale.daysShort[weekdayIndex] : locale.days[weekdayIndex];
      case 'h': return digits(twelveHour ? parts.h % 12 || 12 : parts.h, item.size);
      case 'n': return digits(parts.mi, item.size);
      default: return digits(parts.s, item.size);
    }
  }).join('');
}

// A number's digits rounded to some places, as decimal text rather than as a binary fraction: to
// fifteen significant digits first, the precision a spreadsheet keeps, so 1.005 rounds to 1.01 the
// way it reads rather than to the 1.00 its binary value would.
function roundedDigits(value, places) {
  const [mantissa, exponent] = value.toExponential(14).split('e');
  const digits = mantissa.replace('.', '');
  const point = Number(exponent) + 1;
  let whole;
  let fraction;
  if (point <= 0) {
    whole = '0';
    fraction = '0'.repeat(-point) + digits;
  } else if (point >= digits.length) {
    whole = digits + '0'.repeat(point - digits.length);
    fraction = '';
  } else {
    whole = digits.slice(0, point);
    fraction = digits.slice(point);
  }

  if (fraction.length > places) {
    const up = fraction.charCodeAt(places) >= 53;
    fraction = fraction.slice(0, places);
    if (up) {
      const all = (whole + fraction).split('');
      let at = all.length - 1;
      while (at >= 0 && all[at] === '9') {
        all[at] = '0';
        at -= 1;
      }
      if (at < 0) all.unshift('1');
      else all[at] = String(Number(all[at]) + 1);
      const joined = all.join('');
      whole = joined.slice(0, joined.length - places);
      fraction = joined.slice(joined.length - places);
    }
  } else {
    fraction = fraction.padEnd(places, '0');
  }
  return { whole: whole.replace(/^0+(?=\d)/, ''), fraction };
}

const filler = (slot) => (slot.digit === '0' ? '0' : slot.digit === '?' ? ' ' : '');

function formatNumber(value, allTokens, locale) {
  const sections = sectionsOf(allTokens);
  let tokens = sections[0];
  let magnitude = value;
  let sign = '';
  if (value < 0) {
    magnitude = -value;
    if (sections.length > 1) tokens = sections[1];
    else sign = '-';
  } else if (value === 0 && sections.length > 2) {
    tokens = sections[2];
  }

  const items = [];
  let region = 'whole';
  let percent = 0;
  let exponentSign = null;
  for (let at = 0; at < tokens.length; at += 1) {
    const token = tokens[at];
    if (token.literal !== undefined) {
      items.push({ literal: token.literal });
      continue;
    }
    const { code } = token;
    if (code === '0' || code === '#' || code === '?') items.push({ digit: code, region });
    else if (code === '.' && region === 'whole') {
      region = 'fraction';
      items.push({ point: true });
    } else if (code === ',' && region === 'whole') items.push({ comma: true });
    else if ((code === 'E' || code === 'e') && (tokens[at + 1]?.code === '+' || tokens[at + 1]?.code === '-')
      && region !== 'exponent') {
      exponentSign = tokens[at + 1].code;
      at += 1;
      region = 'exponent';
      items.push({ exponent: true });
    } else {
      if (code === '%') percent += 1;
      items.push({ literal: code });
    }
  }

  const wholeSlots = items.filter((item) => item.digit && item.region === 'whole');
  const fractionSlots = items.filter((item) => item.digit && item.region === 'fraction');
  const exponentSlots = items.filter((item) => item.digit && item.region === 'exponent');

  // A comma between two digits groups the thousands. A comma after the last of them divides by a
  // thousand, which is how `#,##0,"k"` writes thousands.
  let grouping = false;
  let scale = 0;
  items.forEach((item, index) => {
    if (!item.comma) return;
    const before = items.slice(0, index).some((other) => other.digit && other.region === 'whole');
    const after = items.slice(index + 1).some((other) => other.digit && other.region === 'whole');
    if (before && after) grouping = true;
    else if (before) scale += 1;
    else item.literal = ',';
  });

  magnitude = (magnitude * 100 ** percent) / 1000 ** scale;

  let exponent = 0;
  const wholeWidth = Math.max(1, wholeSlots.length);
  if (exponentSign !== null && magnitude !== 0) {
    exponent = Math.floor(Math.log10(magnitude)) - (wholeWidth - 1);
    magnitude /= 10 ** exponent;
  }
  // A number past what a double can scale - a percent of 1e308, the exponent of the smallest one there
  // is - has no digits to place. It is written as the number it is rather than as nonsense.
  if (!Number.isFinite(magnitude)) return String(value).replace('.', () => locale.decimal);
  let { whole, fraction } = roundedDigits(magnitude, fractionSlots.length);
  if (exponentSign !== null && whole.length > wholeWidth) {
    exponent += 1;
    ({ whole, fraction } = roundedDigits(magnitude / 10, fractionSlots.length));
  }

  // The whole number fills its slots from the right, and the first slot takes whatever is left, so
  // `0` still writes 1234 in full. A zero whole part is written only where a `0` asks for one.
  const wholeText = new Map();
  let remaining = whole === '0' ? '' : whole;
  for (let index = wholeSlots.length - 1; index >= 0; index -= 1) {
    const slot = wholeSlots[index];
    if (index === 0) {
      wholeText.set(slot, remaining || filler(slot));
      remaining = '';
    } else if (remaining) {
      wholeText.set(slot, remaining.slice(-1));
      remaining = remaining.slice(0, -1);
    } else {
      wholeText.set(slot, filler(slot));
    }
  }
  const groupedWhole = () => {
    const joined = wholeSlots.map((slot) => wholeText.get(slot)).join('');
    const [, lead, digits] = /^(\D*)(\d*)$/.exec(joined) ?? [, '', joined];
    // A function, so a separator is written as it is: as a replacement, `$&` would mean the match.
    return lead + digits.replace(/\B(?=(\d{3})+(?!\d))/g, () => locale.thousands);
  };

  // Optional places are dropped from the right while they would only write a zero.
  const fractionText = new Map();
  let trailing = true;
  for (let index = fractionSlots.length - 1; index >= 0; index -= 1) {
    const slot = fractionSlots[index];
    const digit = fraction[index];
    if (trailing && digit === '0' && slot.digit !== '0') fractionText.set(slot, filler(slot));
    else {
      trailing = false;
      fractionText.set(slot, digit);
    }
  }

  const exponentZeros = exponentSlots.filter((slot) => slot.digit === '0').length;
  const exponentDigits = pad(Math.abs(exponent), exponentZeros);

  let wroteWhole = false;
  const written = items.map((item) => {
    if (item.literal !== undefined) return item.literal;
    if (item.comma) return '';
    if (item.point) {
      // A format with no whole slots still writes the whole number, in front of the point.
      const lead = !wholeSlots.length && whole !== '0' ? whole : '';
      return lead + locale.decimal;
    }
    if (item.exponent) return `E${exponent < 0 ? '-' : exponentSign === '+' ? '+' : ''}`;
    if (item.region === 'whole') {
      if (!grouping) return wholeText.get(item);
      if (wroteWhole) return '';
      wroteWhole = true;
      return groupedWhole();
    }
    if (item.region === 'fraction') return fractionText.get(item);
    if (item === exponentSlots[0]) return exponentDigits;
    return '';
  }).join('');

  return sign + written;
}

// Text given a number format is written as it is, unless the format has a section for text, where
// `@` is the text.
function formatText(value, allTokens) {
  const sections = sectionsOf(allTokens);
  const section = sections.length >= 4 ? sections[3]
    : sections.length === 1 && sections[0].some((token) => token.code === '@') ? sections[0] : null;
  if (!section) return text(value);
  return section.map((token) => token.literal ?? (token.code === '@' ? text(value) : token.code)).join('');
}

// A number in the plain form a program writes one first - data from SQL arrives as `1234.5` whatever
// the reader's language - and only then as a person in the locale writes one.
function numericValue(value, locale) {
  if (typeof value === 'number') return Number.isFinite(value) ? value : null;
  if (typeof value !== 'string') return null;
  const written = value.trim();
  if (written.length > LONGEST_NUMBER) return null;
  if (PLAIN_NUMBER.test(written)) return Number.isFinite(Number(written)) ? Number(written) : null;
  return parseNumber(written, locale);
}

const notADate = (value) => error(ERROR.VALUE, `"${text(value)}" is not a date or a time.`);

/**
 * A value written with a format, in a locale: what `text(value, format)` does in a formula, for a
 * module that wants the same without one. Returns an error value when a date format is given
 * something that is not a date.
 */
export function format(value, pattern, locale = localeFor()) {
  if (isError(value)) return value.code;
  if (value === null || value === undefined || value === '') return '';

  const named = NAMED_FORMATS.get(String(pattern ?? '').replace(/[\s_-]/g, '').toLowerCase());

  if (named?.general === 'number') {
    const found = numericValue(value, locale);
    return found === null ? text(value)
      : String(Number(found.toPrecision(15))).replace('.', () => locale.decimal);
  }

  if (named?.general === 'date') {
    const parts = dateParts(value, locale);
    if (!parts) return notADate(value);
    const dateText = formatDate(parts, scanFormat(locale.shortDate), locale);
    const timeText = formatDate(parts, scanFormat(locale.longTime), locale);
    if (parts.kind === 'date') return dateText;
    if (parts.kind === 'time') return timeText;
    // A date and time at midnight is written as the date, as Access writes one.
    return parts.h || parts.mi || parts.s ? `${dateText} ${timeText}` : dateText;
  }

  const tokens = scanFormat(named?.pattern ?? (named?.locale ? locale[named.locale] : pattern));
  if (isDateFormat(tokens)) {
    const parts = dateParts(value, locale);
    return parts ? formatDate(parts, sectionsOf(tokens)[0], locale) : notADate(value);
  }
  const found = typeof value === 'boolean' ? null : numericValue(value, locale);
  // `@` with no digit to place is the value as text, a number as much as anything else.
  const textOnly = tokens.some((token) => token.code === '@')
    && !tokens.some((token) => /^[0#?]$/.test(token.code ?? ''));
  return found === null || textOnly ? formatText(value, tokens) : formatNumber(found, tokens, locale);
}

/**
 * Every function an expression can call, by the name it is called with. The table has no prototype
 * and cannot be changed: an expression asking for `constructor` or `toString` must find nothing
 * here rather than something JavaScript put there.
 */
export const FUNCTIONS = Object.freeze(Object.assign(Object.create(null), {
  min,
  max,
  sum,
  average,
  count,
  iferror,
  round,
  floor,
  ceil,
  abs,
  if: choose,
  coalesce,
  concat,
  upper,
  lower,
  trim,
  len,
  number,
  numbervalue,
  text,
  json,
  today,
  now,
  date,
  time,
  datevalue,
  timevalue,
  year,
  month,
  day,
  hour,
  minute,
  second,
  weekday,
  edate,
  eomonth,
  dateadd,
  datediff,
}));
