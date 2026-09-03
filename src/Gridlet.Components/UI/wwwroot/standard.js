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

/** Null and undefined are the absence of a value, not the words "null" and "undefined". */
export function text(value) {
  if (value === null || value === undefined) return '';
  if (isError(value)) return value.code;
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

export const concat = (...values) => values.map(text).join('');
export const upper = (value) => text(value).toUpperCase();
export const lower = (value) => text(value).toLowerCase();
export const trim = (value) => text(value).trim();
export const len = (value) => text(value).length;

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
  text,
  json,
}));
