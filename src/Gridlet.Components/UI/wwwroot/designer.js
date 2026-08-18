// Gridlet components designer.
//
// Loaded by the Gridlet shell when the Gridlet.Components package is installed. Everything it needs
// from the shell arrives through window.gridlet; it reaches into nothing else.
//
// The document this writes is the artifact: readable, diffable, and versioned by SCHEMA_VERSION.
// Renderers here are shared in spirit with the eventual runtime — the designer draws a control the
// same way a published component will, so what you see is what runs.

(() => {
  'use strict';

  const {
    h, api, post, del, toast, modal, confirmModal,
    registerSidebarSection, registerTabRestorer, openTab, closeTab, refreshTabs, state,
  } = window.gridlet;

  const SCHEMA_VERSION = 1;
  const GRID = 8;

  // Where the workspace is served from. A dynamic import() inside a classic script resolves against
  // this file's own URL rather than the page's, so URLs for anything the workspace serves are built
  // from a known root instead of being left to resolve into the module folder.
  const WORKSPACE_ROOT = new URL('../../../', document.currentScript?.src || document.baseURI).href;

  // How a component arranges its controls. A property of the component, not a mode of the editor, so it
  // travels with the document. Grid and flex layouts join this table when they are implemented.
  const LAYOUTS = { free: 'Freeform' };

  // The pages of the properties panel. Appearance is how it looks; Settings is everything else it
  // is — what it is called, what it shows, and how it behaves. What a thing displays and what it
  // is called were two pages and are now two groups on one, because binding a value and naming the
  // control you are binding it in are the same piece of work and were a tab apart.
  // ---- icons ----
  // Tabler Icons, outline set, copied in as path data. Copying beats depending: the designer ships
  // inside the assembly as one script and one stylesheet, and it stays that way. Each entry is the
  // icon's own name in the set, so the original is one search away when one needs replacing.
  //
  // Tabler Icons - MIT Licence, Copyright (c) 2020-2026 Pawel Kuna. Credited in About > Licences.
  //
  // Weight, colour, caps and joins come from the stylesheet, so an icon looks like the control it
  // sits in rather than carrying its own idea of either.

  const iconPath = (d) => '<path d="' + d + '"></path>';

  const ICONS = {
    adjustments: [
      'M4 10a2 2 0 1 0 4 0a2 2 0 0 0 -4 0', 'M6 4v4', 'M6 12v8',
      'M10 16a2 2 0 1 0 4 0a2 2 0 0 0 -4 0', 'M12 4v10', 'M12 18v2',
      'M16 7a2 2 0 1 0 4 0a2 2 0 0 0 -4 0', 'M18 4v1', 'M18 9v11',
    ].map(iconPath).join(''),
    contrast: [
      'M3 12a9 9 0 1 0 18 0a9 9 0 1 0 -18 0', 'M12 17a5 5 0 0 0 0 -10v10',
    ].map(iconPath).join(''),
    'grid-3x3': ['M3 8h18', 'M3 16h18', 'M8 3v18', 'M16 3v18'].map(iconPath).join(''),
    'layout-sidebar-right': [
      'M4 6a2 2 0 0 1 2 -2h12a2 2 0 0 1 2 2v12a2 2 0 0 1 -2 2h-12a2 2 0 0 1 -2 -2l0 -12',
      'M15 4l0 16',
    ].map(iconPath).join(''),
    magnet: [
      'M4 13v-8a2 2 0 0 1 2 -2h1a2 2 0 0 1 2 2v8a2 2 0 0 0 6 0v-8a2 2 0 0 1 2 -2h1a2 2 0 0 1 2 2v8a8 8 0 0 1 -16 0',
      'M4 8l5 0', 'M15 8l4 0',
    ].map(iconPath).join(''),
    moon: [
      'M12 3c.132 0 .263 0 .393 0a7.5 7.5 0 0 0 7.92 12.446a9 9 0 1 1 -8.313 -12.454l0 .008',
    ].map(iconPath).join(''),
    palette: [
      'M12 21a9 9 0 0 1 0 -18c4.97 0 9 3.582 9 8c0 1.06 -.474 2.078 -1.318 2.828c-.844 .75 -1.989 1.172 -3.182 1.172h-2.5a2 2 0 0 0 -1 3.75a1.3 1.3 0 0 1 -1 2.25',
      'M7.5 10.5a1 1 0 1 0 2 0a1 1 0 1 0 -2 0',
      'M11.5 7.5a1 1 0 1 0 2 0a1 1 0 1 0 -2 0',
      'M15.5 10.5a1 1 0 1 0 2 0a1 1 0 1 0 -2 0',
    ].map(iconPath).join(''),
    sun: [
      'M8 12a4 4 0 1 0 8 0a4 4 0 1 0 -8 0',
      'M3 12h1m8 -9v1m8 8h1m-9 8v1m-6.4 -15.4l.7 .7m12.1 -.7l-.7 .7m0 11.4l.7 .7m-12.1 -.7l-.7 .7',
    ].map(iconPath).join(''),
  };

  const TABS = [
    { id: 'appearance', label: 'Appearance', icon: ICONS.palette },
    { id: 'settings', label: 'Settings', icon: ICONS.adjustments },
  ];

  // Icons are SVG fragments rather than text glyphs, so they stay legible at tab size instead of
  // depending on whatever the operating system happens to have. The markup is written in this
  // file, never taken from data.
  function svgIcon(markup, className = 'gfd-tab-icon') {
    const wrapper = h('span', { class: className });
    wrapper.innerHTML = `<svg viewBox="0 0 24 24" aria-hidden="true" focusable="false">${markup}</svg>`;
    return wrapper;
  }

  // ---- JavaScript editing ------------------------------------------------------
  // The workspace has no editor to borrow that is not SQL's, so this is the same arrangement it
  // uses: a transparent textarea over a highlighted copy of its own text. No dependency, no build
  // step, and the browser's own editing behaviour underneath.

  const JS_KEYWORDS = new Set([
    'as', 'async', 'await', 'break', 'case', 'catch', 'class', 'const', 'continue', 'debugger',
    'default', 'delete', 'do', 'else', 'export', 'extends', 'false', 'finally', 'for', 'from',
    'function', 'get', 'if', 'import', 'in', 'instanceof', 'let', 'new', 'null', 'of', 'return',
    'set', 'static', 'super', 'switch', 'this', 'throw', 'true', 'try', 'typeof', 'undefined',
    'var', 'void', 'while', 'with', 'yield',
  ]);

  const escapeHtml = (text) => text.replace(/[&<>]/g,
    (character) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;' })[character]);

  // Enough to read code by: comments, strings and templates, numbers, keywords, private names,
  // calls and constructors. It is a highlighter, not a parser, and does not pretend otherwise.
  function highlightJs(source) {
    const pattern = /(\/\/[^\n]*|\/\*[\s\S]*?\*\/)|(`(?:\\[\s\S]|[^`\\])*`?|'(?:\\[\s\S]|[^'\\\n])*'?|"(?:\\[\s\S]|[^"\\\n])*"?)|(\b\d[\w.]*)|(#?[A-Za-z_$][\w$]*)/g;
    let output = '';
    let at = 0;

    for (const match of source.matchAll(pattern)) {
      const [text, comment, string, number, word] = match;
      output += escapeHtml(source.slice(at, match.index));
      at = match.index + text.length;

      if (comment) output += `<span class="gfd-tok-comment">${escapeHtml(text)}</span>`;
      else if (string) output += `<span class="gfd-tok-string">${escapeHtml(text)}</span>`;
      else if (number) output += `<span class="gfd-tok-number">${escapeHtml(text)}</span>`;
      else if (word) {
        const called = /^\s*\(/.test(source.slice(at));
        const type = JS_KEYWORDS.has(text) ? 'keyword'
          : text.startsWith('#') ? 'private'
            : called ? 'function'
              : /^[A-Z]/.test(text) ? 'type' : null;
        output += type
          ? `<span class="gfd-tok-${type}">${escapeHtml(text)}</span>`
          : escapeHtml(text);
      }
    }

    return output + escapeHtml(source.slice(at));
  }

  // The same idea for a stylesheet: comments, strings, at-rules, the property side of a
  // declaration and the value side of one. Where a word sits decides what it is — a name before a
  // colon inside braces is a property, the same name outside them is part of a selector — so the
  // scanner carries just enough state to know which side of a declaration it is on.
  function highlightCss(source) {
    const pattern = /(\/\*[\s\S]*?\*\/)|("(?:\\[\s\S]|[^"\\])*"?|'(?:\\[\s\S]|[^'\\])*'?)|(@[A-Za-z-]+|!important)|([{}:;])|(--[A-Za-z0-9_-]+|[A-Za-z_-][\w-]*(?=\s*\()|[A-Za-z_-][\w-]*)|(#[0-9A-Fa-f]{3,8}\b|-?\d[\w.%]*)/g;
    let output = '';
    let at = 0;
    let depth = 0;
    let inValue = false;

    for (const match of source.matchAll(pattern)) {
      const [text, comment, string, keyword, punctuation, word, number] = match;
      output += escapeHtml(source.slice(at, match.index));
      at = match.index + text.length;

      const paint = (type) => `<span class="gfd-tok-${type}">${escapeHtml(text)}</span>`;

      if (comment) output += paint('comment');
      else if (string) output += paint('string');
      else if (keyword) output += paint('keyword');
      else if (punctuation) {
        if (text === '{') { depth += 1; inValue = false; }
        else if (text === '}') { depth = Math.max(0, depth - 1); inValue = false; }
        else if (text === ':') { if (depth) inValue = true; }
        else inValue = false;
        output += escapeHtml(text);
      } else if (number) output += paint('number');
      else if (word) {
        const called = /^\s*\(/.test(source.slice(at));
        const type = text.startsWith('--') ? 'private'
          : called ? 'function'
            : depth === 0 ? 'type'
              : inValue ? null : 'property';
        output += type ? paint(type) : escapeHtml(text);
      }
    }

    return output + escapeHtml(source.slice(at));
  }

  // ---- modules -----------------------------------------------------------------
  // A module is a file in the workspace, not a property of one component: it opens in its own tab, it
  // is edited on its own, and any component can name it. The designer says which modules a component runs;
  // this is where they are written.

  const scriptApi = {
    list: () => api('api/components/scripts'),
    read: (name) => api('api/components/scripts/' + encodeURIComponent(name)),
    save: (name, source) => api('api/components/scripts/' + encodeURIComponent(name), {
      method: 'PUT',
      body: JSON.stringify({ source }),
    }),
    remove: (name) => del('api/components/scripts/' + encodeURIComponent(name)),
  };

  // Components that are currently running, so saving a module can put the new version straight in front
  // of whoever is looking at a component that uses it. The alternative is telling people to go and press
  // something, which is the kind of step that gets left out of the instructions.
  const runningComponents = new Set();

  // Every open designer, running or not. A module's exports are what its component's expressions call,
  // so renaming or adding an export has to reach a component that is being drawn as well as one that is
  // being filled in.
  const openComponents = new Set();

  // A component names the behaviour it runs. An entry is either the file — meaning the class it exports
  // as its default — or the file and one class in it, which is how one file holds the behaviour of
  // two components without either of them running the other's. A plain name is the older spelling and
  // keeps its older meaning, so nothing has to be rewritten to keep working.
  const moduleFileOf = (entry) => (typeof entry === 'string' ? entry : entry?.module || '');
  const moduleClassOf = (entry) => (typeof entry === 'string' ? null : entry?.class || null);

  function moduleSaved(name) {
    for (const component of openComponents) {
      if (!component.usesModule(name)) continue;
      // Running components reload the module anyway on the way to restarting, so asking twice would
      // fetch it twice and hand the component two copies of the same file.
      if (runningComponents.has(component)) component.restart();
      else component.refreshScope();
    }
  }

  const CLASS_NAME = (name) => name.replace(/\.js$/i, '').split(/[^A-Za-z0-9]+/)
    .filter(Boolean)
    .map((part) => part[0].toUpperCase() + part.slice(1))
    .join('') || 'ComponentBehaviour';

  // A new module starts as something that already runs: the component it was handed, and somewhere to
  // put what happens when that component is ready.
  const STARTER_MODULE = (name) => `// ${name}
//
// Behaviour for a Gridlet component. The component is handed to you; the rest is ordinary JavaScript —
// import what you like, export what you like, keep what is yours private.

// What a formula can call. Anything exported here can be named in a property or in a handler on
// any control: =initials(data.FirstName, data.LastName). A formula hands a function what it needs,
// so \`component\` is an argument like any other.
export function initials(first, last) {
  return String(first ?? '').slice(0, 1) + String(last ?? '').slice(0, 1);
}

// What the component runs. The class is this component's behaviour; it is given the component and keeps it.
// The constructor stores, connected() acts: the class is built while the component is being designed
// too, so that its methods answer a formula there as well as in Preview.
export default class ${CLASS_NAME(name)} {
  #component;

  // The second argument is optional: services.notify, services.http, services.storage, and
  // anything a module of yours offers as an exported services object.
  constructor(component, services) {
    this.#component = component;
  }

  // A public method is a name a formula can call: =size(), or =${CLASS_NAME(name)}.size() when
  // something else in this component has a size of its own. It is bound to this instance, so it can
  // read what the constructor kept.
  size() {
    const box = this.#component.element.getBoundingClientRect();
    return Math.round(box.width) + ' x ' + Math.round(box.height);
  }

  // Called once the component is running and its rows have loaded.
  connected() {
    this.#component.on('row', (row) => this.#rowChanged(row));
  }

  #rowChanged(row) {
    // this.#component.field('total').value = row.Price * row.Quantity;
  }
}
`;

  const VALID_MODULE_NAME = /^[A-Za-z0-9][A-Za-z0-9._-]*\.js$/;

  function newModule(onCreated) {
    const input = h('input', { type: 'text', placeholder: 'behaviour.js', 'data-testid': 'component-code-name' });
    modal('New module', h('div', {},
      h('label', { class: 'field-label' }, h('span', { text: 'File name' }), input),
      h('p', { class: 'field-note' },
        'A JavaScript file a component can run. Modules sit in one folder, so one can import another by name.')),
      [
        { label: 'Cancel', onClick: (close) => close() },
        {
          label: 'Create',
          primary: true,
          onClick: async (close, showError) => {
            const name = input.value.trim().replace(/\.js$/i, '') + '.js';
            if (!VALID_MODULE_NAME.test(name)) {
              showError('Use letters, digits, dots, dashes and underscores, ending in .js.');
              return;
            }
            try {
              const saved = await scriptApi.save(name, STARTER_MODULE(name));
              close();
              await onCreated?.(saved);
            } catch (err) {
              showError(err.message);
            }
          },
        },
      ]);
    setTimeout(() => input.focus(), 0);
  }

  // ---- what a stylesheet offers ------------------------------------------------
  // Enough of CSS to write a component's styling without a reference open beside it. It is not the
  // whole language and does not try to be: it is what a component's own rules reach for, and what each
  // of those properties takes. A property that is not listed is still perfectly good CSS — the
  // list suggests, it does not permit.

  const CSS_VALUES = {
    'align-items': ['center', 'flex-start', 'flex-end', 'stretch', 'baseline'],
    'background': ['none', 'transparent', 'currentColor'],
    'background-color': ['transparent', 'currentColor'],
    'border-style': ['none', 'solid', 'dashed', 'dotted', 'double'],
    'box-sizing': ['border-box', 'content-box'],
    'cursor': ['default', 'pointer', 'text', 'move', 'not-allowed', 'grab', 'help'],
    'display': ['block', 'inline', 'inline-block', 'flex', 'inline-flex', 'grid', 'none', 'contents'],
    'flex-direction': ['row', 'column', 'row-reverse', 'column-reverse'],
    'flex-wrap': ['nowrap', 'wrap', 'wrap-reverse'],
    'font-style': ['normal', 'italic', 'oblique'],
    'font-weight': ['normal', 'bold', '100', '300', '400', '500', '600', '700', '900'],
    'justify-content': ['center', 'flex-start', 'flex-end', 'space-between', 'space-around', 'space-evenly'],
    'overflow': ['visible', 'hidden', 'auto', 'scroll', 'clip'],
    'overflow-x': ['visible', 'hidden', 'auto', 'scroll', 'clip'],
    'overflow-y': ['visible', 'hidden', 'auto', 'scroll', 'clip'],
    'pointer-events': ['auto', 'none'],
    'position': ['static', 'relative', 'absolute', 'fixed', 'sticky'],
    'resize': ['none', 'both', 'horizontal', 'vertical'],
    'text-align': ['left', 'center', 'right', 'justify', 'start', 'end'],
    'text-decoration': ['none', 'underline', 'line-through', 'overline'],
    'text-overflow': ['clip', 'ellipsis'],
    'text-transform': ['none', 'uppercase', 'lowercase', 'capitalize'],
    'user-select': ['auto', 'none', 'text', 'all'],
    'vertical-align': ['baseline', 'middle', 'top', 'bottom'],
    'visibility': ['visible', 'hidden', 'collapse'],
    'white-space': ['normal', 'nowrap', 'pre', 'pre-wrap', 'pre-line'],
    'word-break': ['normal', 'break-word', 'break-all', 'keep-all'],
  };

  const CSS_PROPERTIES = [...new Set([
    ...Object.keys(CSS_VALUES),
    'align-self', 'animation', 'aspect-ratio', 'backdrop-filter', 'background-image',
    'background-position', 'background-repeat', 'background-size', 'border', 'border-bottom',
    'border-color', 'border-left', 'border-radius', 'border-right', 'border-top', 'border-width',
    'bottom', 'box-shadow', 'caret-color', 'clip-path', 'color', 'column-gap', 'content', 'filter',
    'flex', 'flex-basis', 'flex-grow', 'flex-shrink', 'font-family', 'font-size', 'gap',
    'grid-column', 'grid-row', 'grid-template-columns', 'grid-template-rows', 'height', 'inset',
    'justify-self', 'left', 'letter-spacing', 'line-height', 'margin', 'margin-bottom',
    'margin-left', 'margin-right', 'margin-top', 'max-height', 'max-width', 'min-height',
    'min-width', 'object-fit', 'opacity', 'order', 'outline', 'outline-color', 'outline-offset',
    'padding', 'padding-bottom', 'padding-left', 'padding-right', 'padding-top', 'place-items',
    'right', 'rotate', 'row-gap', 'scale', 'text-shadow', 'top', 'transform', 'transform-origin',
    'transition', 'translate', 'width', 'z-index',
  ])].sort();

  // What every property takes as well as its own values.
  const CSS_GLOBALS = ['inherit', 'initial', 'revert', 'unset'];

  // The variables the designer itself writes on every control, named here so the value that
  // overrides one can be typed rather than remembered. They are documented in the Generated CSS
  // block beside the box; this is the same list, offered where it is being used.
  const GRIDLET_VARIABLES = [
    '--gfd-left', '--gfd-top', '--gfd-width', '--gfd-height', '--gfd-color', '--gfd-fill',
  ];

  // What can follow a selector. Offered when a colon is typed where a selector is being written,
  // which is the moment it is wanted and no earlier.
  const CSS_PSEUDOS = [
    ':hover', ':focus', ':focus-visible', ':active', ':disabled', ':checked', ':first-child',
    ':last-child', ':nth-child()', ':not()', ':empty', '::before', '::after', '::placeholder',
  ];

  // ---- the editing surface -----------------------------------------------------
  // What a module tab and a stylesheet tab have in common: numbered lines, a highlighted copy of
  // the text under a transparent textarea, and the two kept in step. What differs is the
  // highlighter and what completes, so both arrive as arguments and nothing else here knows which
  // language it is showing.

  function codeSurface({ paint, label, testId, onInput }) {
    const lines = h('div', { class: 'gfd-code-lines', 'aria-hidden': 'true' });
    const highlight = h('pre', { class: 'gfd-code-highlight', 'aria-hidden': 'true' });

    const input = h('textarea', {
      class: 'gfd-code-input',
      spellcheck: 'false',
      autocomplete: 'off',
      autocapitalize: 'off',
      'data-testid': testId,
      'aria-label': label,
      oninput: () => {
        refresh();
        onInput?.();
      },
    });

    function refresh() {
      // A trailing newline leaves the highlighted copy one line short, so it is given something to
      // hold that line open with.
      highlight.innerHTML = paint(input.value) + (input.value.endsWith('\n') ? ' ' : '');
      const count = Math.max(1, input.value.split('\n').length);
      lines.textContent = Array.from({ length: count }, (_, index) => index + 1).join('\n');
    }

    input.addEventListener('scroll', () => {
      highlight.scrollTop = input.scrollTop;
      highlight.scrollLeft = input.scrollLeft;
      lines.scrollTop = input.scrollTop;
    });

    input.addEventListener('keydown', (event) => {
      // Two spaces, because that is what everything else here is written in. Tabbing out of an
      // editor mid-word is never what anybody meant.
      if (event.key === 'Tab' && !event.shiftKey && !event.ctrlKey && !event.metaKey
        && !event.defaultPrevented) {
        event.preventDefault();
        input.setRangeText('  ', input.selectionStart, input.selectionEnd, 'end');
        input.dispatchEvent(new Event('input', { bubbles: true }));
      }
    });

    const surface = h('div', { class: 'gfd-code-surface' }, lines, highlight, input);
    return { surface, input, lines, highlight, refresh };
  }

  // Where the caret is on screen, so a list of suggestions can sit under the word being typed.
  // Measured in a copy of the highlighted layer rather than calculated: the copy has the same
  // font, the same wrapping and the same padding as the text being edited, so the marker lands
  // where the caret is instead of where arithmetic thinks it should be.
  function caretPoint(input, highlight) {
    const mirror = highlight.cloneNode(false);
    mirror.style.visibility = 'hidden';
    mirror.style.zIndex = '-1';
    const marker = h('span', { text: '\u200b' });
    mirror.append(document.createTextNode(input.value.slice(0, input.selectionStart)), marker);
    highlight.parentElement.append(mirror);
    const point = { left: marker.offsetLeft, top: marker.offsetTop, height: marker.offsetHeight };
    mirror.remove();
    return {
      left: point.left - input.scrollLeft,
      top: point.top - input.scrollTop,
      height: point.height || 18,
    };
  }

  // ---- completion --------------------------------------------------------------
  // A list of what could come next, under the word being typed. It suggests and never decides:
  // nothing is inserted without Enter or Tab, and Escape puts it away. What it offers comes from
  // the caller, because only the caller knows whether the caret is in a selector, a property or a
  // value — and, for a selector, what this particular component actually contains.

  function attachCompletions(input, highlight, suggest) {
    const list = h('div', {
      class: 'gfd-complete',
      role: 'listbox',
      'data-testid': 'css-completions',
      hidden: '',
    });
    let items = [];
    let active = 0;

    const hide = () => {
      list.hidden = true;
      items = [];
    };

    const render = () => {
      list.replaceChildren(...items.map((item, index) => h('div', {
        class: 'gfd-complete-item' + (index === active ? ' active' : ''),
        role: 'option',
        'aria-selected': String(index === active),
        // Down on the text is what puts the caret somewhere else, so the choice is taken on the
        // way down and the textarea never loses the caret it is about to write at.
        onmousedown: (event) => {
          event.preventDefault();
          accept(index);
        },
      },
        h('span', { class: 'gfd-complete-name', text: item.label }),
        item.detail ? h('span', { class: 'gfd-complete-detail', text: item.detail }) : null)));
    };

    const accept = (index) => {
      const item = items[index];
      if (!item) return;
      const caret = input.selectionStart;
      const start = caret - item.replace.length;
      input.setRangeText(item.insert, start, caret, 'end');
      hide();
      input.dispatchEvent(new Event('input', { bubbles: true }));
      input.focus();
      // A property that has just been named wants its value next, so the list comes straight back
      // with what that property takes.
      if (item.thenSuggest) show(true);
    };

    // Asked for, or typed into. A list that opens on every space and every brace is a list that
    // swallows the Enter meant for a new line, so nothing is offered until there is a word to
    // offer something for — unless it was asked for, which is what Ctrl+Space is.
    function show(explicit = false) {
      const found = suggest(input.value, input.selectionStart) || [];
      items = found.slice(0, 12);
      if (!items.length || (!explicit && !items[0].replace)) {
        hide();
        return;
      }
      active = 0;
      render();
      list.hidden = false;
      const point = caretPoint(input, highlight);
      list.style.left = `${Math.max(0, point.left)}px`;
      // Under the word being typed, or above it when there is no room below: the editing surface
      // clips what leaves it, and a list of suggestions with its bottom half missing is worse than
      // one on the other side of the caret.
      const room = input.clientHeight - (point.top + point.height);
      list.style.top = '0';
      const height = list.offsetHeight;
      list.style.top = height > room && point.top > height
        ? `${point.top - height}px`
        : `${point.top + point.height}px`;
    }

    input.addEventListener('input', () => {
      if (input.selectionStart === input.selectionEnd) show();
      else hide();
    });
    input.addEventListener('blur', hide);
    input.addEventListener('scroll', hide);

    input.addEventListener('keydown', (event) => {
      if (event.ctrlKey && event.key === ' ') {
        event.preventDefault();
        show(true);
        return;
      }
      if (list.hidden) return;
      if (event.key === 'ArrowDown' || event.key === 'ArrowUp') {
        event.preventDefault();
        active = (active + (event.key === 'ArrowDown' ? 1 : items.length - 1)) % items.length;
        render();
        return;
      }
      if (event.key === 'Enter' || event.key === 'Tab') {
        event.preventDefault();
        accept(active);
        return;
      }
      if (event.key === 'Escape') {
        event.preventDefault();
        hide();
      }
    });

    return list;
  }

  // ---- the code tab ----
  // One module, one tab, the same as everything else in the workspace opens. A component designer and
  // the module it runs are two tabs side by side, and saving here reaches a component that is running.

  function openCodeTab(name) {
    openTab({
      key: 'component-script:' + name,
      badge: 'JS',
      title: name,
      render: (panel, tab) => buildCodeTab(panel, tab, name),
      restore: { kind: 'component-script', id: name },
    });
  }

  registerTabRestorer('component-script', async (descriptor) => openCodeTab(descriptor.id));

  // A stylesheet tab belongs to a component that is open: it edits that component's document rather than a
  // file of its own. Coming back after a reload therefore means opening the component first and the
  // stylesheet when the component has built itself, so the request is left here for the designer to
  // pick up. A component tab that is restored but never looked at has not built itself yet, and its
  // stylesheet tab waits with it.
  const pendingCssTabs = new Map();

  const wantCssTab = (componentId, target) => {
    if (!pendingCssTabs.has(componentId)) pendingCssTabs.set(componentId, new Set());
    pendingCssTabs.get(componentId).add(target);
  };

  registerTabRestorer('component-css', async (descriptor) => {
    wantCssTab(descriptor.id, descriptor.target);
    const component = await api('api/components/' + encodeURIComponent(descriptor.id));
    openDesigner(component);
  });

  async function buildCodeTab(panel, tab, name) {
    // Gridlet's own modules open here like any other, and are read rather than written: they are
    // part of the build, so an edit would be lost on the next upgrade.
    let readOnly = false;

    const { surface, input, refresh } = codeSurface({
      paint: highlightJs,
      label: `${name} source`,
      testId: 'component-code-editor',
      onInput: () => {
        if (readOnly) return;
        // The tab bar carries the unsaved mark, so it is redrawn the once the flag turns over
        // rather than on every keystroke after that.
        if (!tab.hasUnsavedDefinition) {
          tab.hasUnsavedDefinition = true;
          refreshTabs();
        }
        saveButton.disabled = false;
      },
    });

    input.addEventListener('keydown', (event) => {
      if ((event.ctrlKey || event.metaKey) && event.key.toLowerCase() === 's') {
        event.preventDefault();
        save();
      }
    });

    const saveButton = h('button', {
      class: 'primary',
      'data-testid': 'component-code-save',
      title: 'Save (Ctrl+S)',
      onclick: () => save(),
    }, 'Save');

    async function save() {
      if (readOnly) return;
      try {
        await scriptApi.save(name, input.value);
        tab.hasUnsavedDefinition = false;
        saveButton.disabled = true;
        refreshTabs();
        // A component running this module picks the new version up without being asked to.
        moduleSaved(name);
        toast(`Saved ${name}.`, false);
      } catch (err) {
        toast(`Failed to save ${name}: ${err.message}`);
      }
    }

    // Asked when the tab is closed, not when it is left. Switching to another tab leaves this one
    // open with the edit still in it, so there is nothing to lose and nothing to ask about; the
    // browser closing is caught by the workspace's own guard on the way out.
    tab.beforeClose = async () => {
      if (!tab.hasUnsavedDefinition) return true;
      return new Promise((resolve) => {
        modal('Unsaved module', h('p', { text: `${name} has unsaved changes.` }), [
          { label: 'Stay', onClick: (close) => { close(); resolve(false); } },
          { label: 'Discard', danger: true, onClick: (close) => { close(); resolve(true); } },
        ]);
      });
    };

    const note = h('span', { class: 'muted gfd-code-note' });

    const deleteButton = h('button', {
      class: 'danger',
      'data-testid': 'component-code-delete',
      onclick: () => confirmModal('Delete module',
        `Delete ${name}? Any component that runs it will stop doing so.`,
        async () => {
          await scriptApi.remove(name);
          await codeSidebar.refresh();
          closeTab(tab.id);
        }),
    }, 'Delete');

    panel.append(h('div', { class: 'gfd-code-tab' },
      h('div', { class: 'viewbar' }, saveButton, note, h('span', { class: 'spacer' }), deleteButton),
      surface));

    saveButton.disabled = true;

    try {
      const script = await scriptApi.read(name);
      input.value = script.source ?? '';
      readOnly = Boolean(script.readOnly);
    } catch (err) {
      input.value = `// ${name} could not be read: ${err.message}\n`;
    }

    if (readOnly) {
      input.readOnly = true;
      surface.classList.add('read-only');
      saveButton.hidden = true;
      deleteButton.hidden = true;
      note.textContent = `${name} — part of Gridlet. Read it, and import it from your own modules.`;
    } else {
      note.textContent = `${name} — runs in any component that names it`;
    }

    refresh();
    input.focus();
  }

  // ---- expressions ------------------------------------------------------------
  // Every property can hold an expression instead of a literal: `self.h`, `data.Total * 1.2`,
  // `button1.x + button1.w + 8`, `if(data.Overdue, 'Overdue', '')`. Expressions are tokenized,
  // parsed and evaluated by this file and nothing else — a saved document is never handed to the
  // JavaScript engine, so it stays data. An expression can reach the values the designer puts in
  // scope and nothing more: no DOM, no network, no globals.

  // The functions an expression can call, and the conversions behind them, come from gridlet.js —
  // the module the workspace serves read-only and lists beside your own. It is deliberately not a
  // copy: what a component author reads there is the code that runs their expression, and it cannot
  // drift from it. It is loaded once, before any component is opened. A component adds its own modules'
  // exports to this set; see the expression scope in the designer.
  let asText;
  let asNumber;
  let truthy;
  let toJson;
  let isError = () => false;
  let makeError;
  let ERROR = {};
  let FUNCTIONS = Object.create(null);

  const STANDARD_LIBRARY = 'gridlet.js';

  const standardLibrary = import(`${WORKSPACE_ROOT}api/components/modules/std/${STANDARD_LIBRARY}`)
    .then((library) => {
      asText = library.text;
      asNumber = library.number;
      truthy = library.truthy;
      toJson = library.json;
      isError = library.isError;
      makeError = library.error;
      ERROR = library.ERROR;
      FUNCTIONS = library.FUNCTIONS;
    })
    .catch((err) => {
      toast(`Gridlet's component functions failed to load: ${err.message}`);
      throw err;
    });

  function isNumeric(value) {
    if (typeof value === 'number') return Number.isFinite(value);
    if (typeof value !== 'string' || !value.trim()) return false;
    return Number.isFinite(Number(value));
  }

  const CONSTANTS = { true: true, false: false, null: null };

  function tokenize(source) {
    // A number may be written the way a spreadsheet writes one, exponent included: 1e308 is a
    // number, not the name `e308` sitting next to a 1.
    const pattern = /(\s+)|(\d+(?:\.\d+)?(?:[eE][-+]?\d+)?)|([A-Za-z_][A-Za-z0-9_]*)|'([^']*)'|"([^"]*)"|(<=|>=|==|!=|&&|\|\||[-+*/%().,?:<>![\]])/y;
    const tokens = [];
    let at = 0;
    while (at < source.length) {
      pattern.lastIndex = at;
      const match = pattern.exec(source);
      if (!match) throw new Error(`Unexpected character "${source[at]}"`);
      at = pattern.lastIndex;
      const [, space, number, name, single, double, operator] = match;
      if (space !== undefined) continue;
      if (number !== undefined) tokens.push({ type: 'number', value: Number(number) });
      else if (name !== undefined) tokens.push({ type: 'name', value: name });
      else if (single !== undefined) tokens.push({ type: 'string', value: single });
      else if (double !== undefined) tokens.push({ type: 'string', value: double });
      else tokens.push({ type: operator });
    }
    return tokens;
  }

  // Precedence climbing, weakest binding outermost, so the tree matches how the expression reads.
  function parse(tokens) {
    let at = 0;
    const peek = () => tokens[at]?.type;
    const eat = (type) => {
      if (tokens[at]?.type === type) { at += 1; return true; }
      return false;
    };
    const expect = (type) => {
      if (!eat(type)) throw new Error(`Expected "${type}"`);
    };

    // A reference is a dotted path, with brackets for names an identifier cannot spell — a column
    // called "Order Date" is written data["Order Date"].
    function path(first) {
      const parts = [first];
      for (;;) {
        if (eat('.')) {
          const token = tokens[at];
          if (token?.type !== 'name') throw new Error('Expected a name after "."');
          parts.push(token.value);
          at += 1;
        } else if (eat('[')) {
          const token = tokens[at];
          if (token?.type !== 'string' && token?.type !== 'number') {
            throw new Error('Expected a name in brackets');
          }
          parts.push(String(token.value));
          at += 1;
          expect(']');
        } else {
          return { kind: 'path', parts };
        }
      }
    }

    // The bracketed arguments of a call, the open bracket already eaten.
    function argumentList() {
      const args = [];
      if (eat(')')) return args;
      do { args.push(ternary()); } while (eat(','));
      expect(')');
      return args;
    }

    function primary() {
      const token = tokens[at];
      if (!token) throw new Error('The expression is unfinished');
      if (token.type === 'number' || token.type === 'string') {
        at += 1;
        return { kind: 'literal', value: token.value };
      }
      if (eat('(')) {
        const node = ternary();
        expect(')');
        return node;
      }
      if (token.type === 'name') {
        at += 1;
        const name = token.value;
        if (peek() === '(') {
          at += 1;
          return { kind: 'call', name, args: argumentList() };
        }
        // A qualified call. `My.total()` is the method of the class called My, `tax.total()` is what
        // tax.js exports, and `gridlet.total()` is Gridlet's own. Only a name, a dot, a name and an
        // open bracket is one: `component.rows` and `button1.right` are paths, and the two are told apart
        // by what follows the dot rather than by what the names happen to mean. One level only, so
        // `a.b.c()` is not a call at all.
        if (peek() === '.' && tokens[at + 1]?.type === 'name' && tokens[at + 2]?.type === '(') {
          const member = tokens[at + 1].value;
          at += 3;
          return { kind: 'call', qualifier: name, name: member, args: argumentList() };
        }
        const lowered = name.toLowerCase();
        // Own properties only, here and at every other table in this file. `in` and a bare index
        // both walk the prototype chain, which would answer `constructor` or `toString` with real
        // JavaScript — exactly what an expression language that never touches JavaScript must not
        // hand back.
        if (Object.hasOwn(CONSTANTS, lowered) && peek() !== '.' && peek() !== '[') {
          return { kind: 'literal', value: CONSTANTS[lowered] };
        }
        return path(name);
      }
      throw new Error(`Unexpected "${token.type}"`);
    }

    function unary() {
      if (eat('-')) return { kind: 'negate', value: unary() };
      if (eat('!')) return { kind: 'not', value: unary() };
      return primary();
    }

    const level = (next, operators) => () => {
      let left = next();
      while (operators.includes(peek())) {
        const operator = peek();
        at += 1;
        left = { kind: 'binary', operator, left, right: next() };
      }
      return left;
    };

    const product = level(unary, ['*', '/', '%']);
    const sum = level(product, ['+', '-']);
    const comparison = level(sum, ['<', '>', '<=', '>=', '==', '!=']);
    const conjunction = level(comparison, ['&&']);
    const disjunction = level(conjunction, ['||']);

    function ternary() {
      const condition = disjunction();
      if (!eat('?')) return condition;
      const then = ternary();
      expect(':');
      return { kind: 'ternary', condition, then, otherwise: ternary() };
    }

    const node = ternary();
    if (at < tokens.length) throw new Error(`Unexpected "${tokens[at].type}" at the end`);
    return node;
  }

  // Parsing is the expensive half and expressions are edited a character at a time, so the tree is
  // kept. Only expressions that parsed are cached; a broken one is re-read until it is fixed. Every
  // keystroke leaves an entry behind, so the cache is emptied rather than allowed to grow all day.
  const parsed = new Map();
  const PARSED_LIMIT = 500;

  function compile(source) {
    if (parsed.has(source)) return parsed.get(source);
    const node = parse(tokenize(source));
    if (parsed.size >= PARSED_LIMIT) parsed.clear();
    parsed.set(source, node);
    return node;
  }

  // `+` adds two numbers and joins anything else, which is how a spreadsheet behaves and how an
  // operator writing `data.First + ' ' + data.Last` expects it to. The rest of the arithmetic is
  // arithmetic: asking for `data.Name * 2` is a mistake, and #VALUE! says so rather than quietly
  // making it zero.
  function applyOperator(operator, left, right) {
    // An error is the answer to anything built on it, so a broken column shows up once and does
    // not turn into a plausible-looking number three properties away.
    const failed = [left, right].find(isError);
    if (failed) return failed;

    const numeric = isNumeric(left) && isNumeric(right);
    const arithmetic = (compute) => {
      if (!numeric) return makeError(ERROR.VALUE, 'This needs numbers on both sides.');
      const result = compute(asNumber(left), asNumber(right));
      return Number.isFinite(result) ? result : makeError(ERROR.NUM, 'The result is not a number.');
    };

    switch (operator) {
      case '+': return numeric ? asNumber(left) + asNumber(right) : asText(left) + asText(right);
      case '-': return arithmetic((a, b) => a - b);
      case '*': return arithmetic((a, b) => a * b);
      case '/':
      case '%':
        if (!numeric) return makeError(ERROR.VALUE, 'This needs numbers on both sides.');
        if (asNumber(right) === 0) return makeError(ERROR.DIV0, 'The right-hand side is zero.');
        return arithmetic(operator === '/' ? (a, b) => a / b : (a, b) => a % b);
      case '&&': return truthy(left) && truthy(right);
      case '||': return truthy(left) ? left : right;
      case '==': return numeric ? asNumber(left) === asNumber(right) : asText(left) === asText(right);
      case '!=': return numeric ? asNumber(left) !== asNumber(right) : asText(left) !== asText(right);
      case '<': return numeric ? asNumber(left) < asNumber(right) : asText(left) < asText(right);
      case '>': return numeric ? asNumber(left) > asNumber(right) : asText(left) > asText(right);
      case '<=': return numeric ? asNumber(left) <= asNumber(right) : asText(left) <= asText(right);
      case '>=': return numeric ? asNumber(left) >= asNumber(right) : asText(left) >= asText(right);
      default: throw new Error(`Unknown operator "${operator}"`);
    }
  }

  // ---- the names an expression can call ----------------------------------------
  // Gridlet's own functions are the floor. A component adds what its modules export and the public
  // methods of the classes it runs, and those are the component author's own definitions. Two of them
  // under one name is not a race to be won by whichever file loaded first: it is a question only
  // the author can answer, so the bare name says it is ambiguous and says how to write it
  // unambiguously. A qualifier names exactly one place to look — the class, the file, or `gridlet`
  // for a built-in the component has written over.

  const GRIDLET = 'gridlet';

  // The name a qualifier can be written with. A file called `my-helpers.js` has no spelling an
  // expression could use, so its exports are reachable by their own names and not by its.
  const QUALIFIER = /^[A-Za-z_][A-Za-z0-9_]*$/;

  function nameScope() {
    // Maps rather than objects, so nothing here answers `constructor` or `toString` with real
    // JavaScript. A name is matched without regard to case; the spelling it was written with is
    // kept, because that is the spelling to show back to whoever wrote it.
    const functions = new Map();
    const values = new Map();
    const groups = new Map();

    const entriesOf = (table, name) => table.get(name.toLowerCase()) || [];

    // "A.total() or B.total()", with commas before the last, so the message reads as a sentence.
    const spell = (entries, name, call) => {
      const written = entries.filter((entry) => entry.qualifier)
        .map((entry) => `${entry.qualifier}.${name}${call ? '()' : ''}`);
      if (!written.length) return null;
      return written.length > 1
        ? `${written.slice(0, -1).join(', ')} or ${written.at(-1)}`
        : written[0];
    };

    const ambiguous = (name, entries, call) => {
      const written = spell(entries, name, call);
      return makeError(ERROR.NAME, written
        ? `"${name}" is ambiguous — write ${written}.`
        : `"${name}" is defined more than once in this component. Rename one of them.`);
    };

    const push = (table, name, entry) => {
      const key = name.toLowerCase();
      if (!table.has(key)) table.set(key, []);
      table.get(key).push(entry);
    };

    return {
      // Takes a qualifier, unless something else already has that name. Qualifiers share one
      // namespace — a class called `tax` in one file beside a `tax.js` in another would make
      // `tax.vat()` two different things — so the second one to ask is refused and told about it.
      // Asking twice for the same file is not that: a class called My in my.js is one file's name
      // written two ways, so the two share the group and `my.LIMIT` and `My.total()` both work.
      claim(label, owner) {
        const key = label.toLowerCase();
        if (key === GRIDLET || !QUALIFIER.test(label)) return false;
        const group = groups.get(key);
        if (group) return group.owner === owner;
        groups.set(key, { label, owner, members: new Map() });
        return true;
      },

      // One definition of the component author's own: under the name it was written with, and under its
      // qualifier when it has one.
      define(kind, spelling, value, qualifier) {
        push(kind === 'value' ? values : functions, spelling, { qualifier, spelling, value });
        const group = qualifier ? groups.get(qualifier.toLowerCase()) : null;
        if (group && !group.members.has(spelling.toLowerCase())) {
          group.members.set(spelling.toLowerCase(), { spelling, value });
        }
      },

      // Every name that ended up meaning more than one thing, so the component can say so once beside
      // the modules it came from instead of only where a formula went looking for it.
      ambiguities() {
        const found = [];
        for (const table of [functions, values]) {
          for (const entries of table.values()) {
            if (entries.length < 2) continue;
            found.push({
              spelling: entries[0].spelling,
              written: spell(entries, entries[0].spelling, table === functions),
            });
          }
        }
        return found;
      },

      // A bare call. One definition of the author's own wins, including over a built-in of the same
      // name: what a component runs is the component author's code. Two of them is ambiguous.
      call(name) {
        const entries = entriesOf(functions, name);
        if (entries.length === 1) return { fn: entries[0].value };
        if (entries.length > 1) return { error: ambiguous(name, entries, true) };
        const key = name.toLowerCase();
        if (Object.hasOwn(FUNCTIONS, key)) return { fn: FUNCTIONS[key] };
        return { error: makeError(ERROR.NAME, `There is no function called "${name}".`) };
      },

      // A qualified call. `gridlet.` always reaches the built-in, which is what makes writing over
      // one safe: the original is still there under the name of the library it came from.
      qualifiedCall(qualifier, name) {
        const key = qualifier.toLowerCase();
        if (key === GRIDLET) {
          const lowered = name.toLowerCase();
          return Object.hasOwn(FUNCTIONS, lowered)
            ? { fn: FUNCTIONS[lowered] }
            : { error: makeError(ERROR.NAME, `Gridlet has no function called "${name}".`) };
        }
        const group = groups.get(key);
        if (!group) {
          return { error: makeError(ERROR.NAME, `There is nothing called "${qualifier}".`) };
        }
        const member = group.members.get(name.toLowerCase());
        if (typeof member?.value !== 'function') {
          return { error: makeError(ERROR.NAME, `"${group.label}" has no function called "${name}".`) };
        }
        return { fn: member.value };
      },

      // A bare name that is not a control and not the row: what a module exports as a value.
      value(name) {
        const entries = entriesOf(values, name);
        if (entries.length === 1) return { found: true, value: entries[0].value };
        if (entries.length > 1) return { error: ambiguous(name, entries, false) };
        return { found: false };
      },

      // The same, qualified: `tax.VAT_RATE` reaches one file's own constant.
      member(qualifier, name) {
        const group = groups.get(String(qualifier).toLowerCase());
        const member = group?.members.get(String(name).toLowerCase());
        return member ? { found: true, value: member.value } : { found: false };
      },
    };
  }

  // What anything evaluated outside a component calls: Gridlet's functions and nothing else.
  const BUILTIN_SCOPE = nameScope();

  // The one function that is meant to see an error rather than be stopped by it.
  const HANDLES_ERRORS = new Set(['iferror']);

  function evaluateNode(node, lookup, scope) {
    switch (node.kind) {
      case 'literal': return node.value;
      case 'path': return lookup(node.parts);

      case 'negate': {
        const value = evaluateNode(node.value, lookup, scope);
        if (isError(value)) return value;
        if (!isNumeric(value)) return makeError(ERROR.VALUE, 'A minus sign needs a number.');
        return -asNumber(value);
      }

      case 'not': {
        const value = evaluateNode(node.value, lookup, scope);
        return isError(value) ? value : !truthy(value);
      }

      case 'ternary': {
        const condition = evaluateNode(node.condition, lookup, scope);
        if (isError(condition)) return condition;
        return truthy(condition)
          ? evaluateNode(node.then, lookup, scope)
          : evaluateNode(node.otherwise, lookup, scope);
      }

      case 'binary': return applyOperator(node.operator,
        evaluateNode(node.left, lookup, scope), evaluateNode(node.right, lookup, scope));

      case 'call': {
        const found = node.qualifier
          ? scope.qualifiedCall(node.qualifier, node.name)
          : scope.call(node.name);
        if (found.error) return found.error;
        const args = node.args.map((argument) => evaluateNode(argument, lookup, scope));
        if (!HANDLES_ERRORS.has(node.name.toLowerCase())) {
          const failed = args.find(isError);
          if (failed) return failed;
        }
        try {
          // Called with no `this`, so a plain function is never handed something it did not ask for.
          // A method is bound to the instance it belongs to before it gets here, and a bound `this`
          // is not something a call can take back — which is the whole difference between a method
          // of the component's behaviour and a loose exported function.
          return Reflect.apply(found.fn, undefined, args);
        } catch (err) {
          // A module's own function is somebody's code and it can throw. That is one property
          // showing #VALUE!, not a component that stops drawing.
          return makeError(ERROR.VALUE, `${node.name} failed: ${err?.message || err}`);
        }
      }

      default: throw new Error('Unreadable expression');
    }
  }

  // The table a call is resolved against comes from the component being evaluated, because a component's own
  // modules add to it. Gridlet's functions on their own are the floor, for anything evaluated
  // outside a component.
  const evaluate = (source, lookup, scope = BUILTIN_SCOPE) =>
    evaluateNode(compile(source), lookup, scope);

  // ---- formula or text ------------------------------------------------------------
  // A property holds one piece of text, the way a spreadsheet cell does. `=` at the front makes it
  // a formula. `'` at the front makes it text whatever it looks like, which is the only way to
  // write a literal that starts with `=`. Everything else is text as typed.
  //
  // The two live in different places once stored: the formula in `bind`, the value it worked out to
  // in the property itself. A reader of the saved document sees real values without evaluating
  // anything, and a formula that breaks has the last good value to fall back on.

  const FORMULA = '=';
  const ESCAPE = "'";

  const isFormula = (value) => typeof value === 'string' && value.trimStart().startsWith(FORMULA);
  const formulaBody = (value) => value.trimStart().slice(FORMULA.length);
  const unescapeText = (value) =>
    (typeof value === 'string' && value.startsWith(ESCAPE) ? value.slice(1) : value);

  // Text that would otherwise read as a formula, or as an escape, is shown with the escape it needs
  // to survive being typed back in.
  const escapeText = (value) => {
    const text = value ?? '';
    return (typeof text === 'string' && (text.startsWith(FORMULA) || text.startsWith(ESCAPE)))
      ? ESCAPE + text
      : text;
  };

  // ---- control catalogue ------------------------------------------------------
  // One entry per control kind: how to create it, how to draw it, and which properties the panel
  // offers. Adding a control means adding an entry here and nothing else.

  // `after` lets a kind react to one of its own properties changing. It returns true when it
  // changed something else the panel is showing, so the panel knows to redraw itself.
  // The events a control answers, offered as properties beside its others. A handler is a formula
  // like anything else here; what makes it a handler is when it runs rather than what it looks
  // like. Handlers are kept apart from properties in the document because a property is worked out
  // on every draw and a handler must run only when the thing it is named for happens.
  const CONTROL_EVENTS = [
    ['click', 'On click', 'When this control is clicked'],
    ['change', 'On change', 'When its value is committed'],
    ['input', 'On input', 'On every keystroke in it'],
    ['focus', 'On focus', 'When it takes the cursor'],
    ['blur', 'On blur', 'When it loses the cursor'],
  ];

  const COMPONENT_EVENTS = [
    ['load', 'On load', 'Once the component is running and its rows have arrived'],
    ['row', 'On row', 'Whenever the row on screen changes'],
    ['resize', 'On resize', 'Whenever the component changes size'],
  ];

  const TEXT = (key, label, after) => ({ key, label, kind: 'text', after });
  const NUMBER = (key, label, after) => ({ key, label, kind: 'number', after });
  const BOOL = (key, label, after) => ({ key, label, kind: 'boolean', after });
  const LINES = (key, label, after) => ({ key, label, kind: 'lines', after });

  // A box that has just been told to hold several lines while it is still one line tall shows
  // none of them. Only the untouched single-line height is grown: a height someone chose, or one
  // an expression decides, is theirs.
  const SINGLE_LINE_HEIGHT = 30;
  const MULTILINE_HEIGHT = 90;

  function fitToMultiline(control) {
    if (!control.props.multiline || control.bind?.h?.trim()) return false;
    if (control.h !== SINGLE_LINE_HEIGHT) return false;
    control.h = MULTILINE_HEIGHT;
    return true;
  }

  // The look Gridlet gives a component field, restated so it can be generated rather than arriving from
  // the shell's element selectors. The values are the workspace's own variables, so a component still
  // follows the theme, and every one of them is visible in the panel and overridable.
  const FIELD_STYLE = {
    background: 'var(--panel-2)',
    color: 'var(--text)',
    border: '1px solid var(--border)',
    'border-radius': '6px',
    padding: '5px 8px',
    'font-family': 'inherit',
    'font-size': '13px',
  };

  // `bindable` controls can display a value; `valueKey` says which of the control's own properties
  // that value is. A label's value is its text — the same property under two names would let one
  // silently overwrite the other — so a label names `text` and the panel edits one thing. Where
  // the displayed value is not a property at all, such as what a text box contains, the control
  // leaves `valueKey` off and `bind` puts the value into the rendered element instead.
  // Binding is read-only for now, so every bind is a display, never a write back.
  //
  // `style` is a control kind's default appearance. It lives here rather than in the designer's
  // stylesheet so it can be emitted into the generated CSS: a default you can read is a default
  // you can override, and nothing gets applied from somewhere the panel never showed you.
  const CATALOGUE = {
    label: {
      title: 'Label',
      icon: 'T',
      bindable: true,
      valueKey: 'text',
      defaults: { w: 120, h: 24, props: { text: 'Label' } },
      style: { display: 'flex', 'align-items': 'center', color: 'var(--text)' },
      properties: [TEXT('text', 'Text')],
      render: (c) => h('span', { class: 'gfd-label', text: c.props.text ?? '' }),
    },
    // One text box, on one line or several. A value that is not a single line — a row read whole,
    // JSON, anything with a newline in it — needs the room, and needing a different control for it
    // would mean rebuilding the box and its bindings just to see the rest of the text.
    textbox: {
      title: 'Text box',
      icon: '▭',
      bindable: true,
      defaults: {
        w: 200,
        h: SINGLE_LINE_HEIGHT,
        props: { placeholder: '', multiline: false, readOnly: false },
      },
      style: (c) => c.props.multiline ? { ...FIELD_STYLE, resize: 'none' } : { ...FIELD_STYLE },
      properties: [
        TEXT('placeholder', 'Placeholder'),
        BOOL('multiline', 'Multiline', fitToMultiline),
        BOOL('readOnly', 'Read only'),
      ],
      render: (c) => c.props.multiline
        ? h('textarea', {
          class: 'gfd-input gfd-textarea', placeholder: c.props.placeholder ?? '',
          readonly: c.props.readOnly ? '' : null, tabindex: '-1',
        })
        : h('input', {
          class: 'gfd-input', type: 'text', placeholder: c.props.placeholder ?? '',
          readonly: c.props.readOnly ? '' : null, tabindex: '-1',
        }),
      bind: (element, value) => { element.value = asText(value); },
    },
    // The text box does this now. The kind stays so documents that used it keep loading and keep
    // being editable; it is out of the palette so there is one way to add one.
    textarea: {
      title: 'Multi-line',
      icon: '▤',
      bindable: true,
      retired: true,
      defaults: { w: 240, h: 90, props: { placeholder: '' } },
      style: { ...FIELD_STYLE, resize: 'none' },
      properties: [TEXT('placeholder', 'Placeholder')],
      render: (c) => h('textarea', {
        class: 'gfd-input gfd-textarea', placeholder: c.props.placeholder ?? '', tabindex: '-1',
      }),
      bind: (element, value) => { element.value = asText(value); },
    },
    checkbox: {
      title: 'Check box',
      icon: '☑',
      bindable: true,
      defaults: { w: 160, h: 24, props: { text: 'Check box' } },
      style: { display: 'flex', 'align-items': 'center', gap: '6px', color: 'var(--text)' },
      properties: [TEXT('text', 'Text')],
      render: (c) => h('label', { class: 'gfd-checkbox' },
        h('input', { type: 'checkbox', tabindex: '-1' }),
        h('span', { text: c.props.text ?? '' })),
      bind: (element, value) => {
        element.querySelector('input').checked = value === true || value === 1 || value === '1'
          || String(value).toLowerCase() === 'true';
      },
    },
    select: {
      title: 'Drop-down',
      icon: '⌄',
      bindable: true,
      defaults: { w: 200, h: 30, props: { options: 'First\nSecond' } },
      style: { ...FIELD_STYLE },
      properties: [LINES('options', 'Options (one per line)')],
      render: (c) => h('select', { class: 'gfd-input', tabindex: '-1' },
        String(c.props.options ?? '').split('\n').filter(Boolean)
          .map((option) => h('option', { text: option }))),
      bind: (element, value) => { element.value = asText(value); },
    },
    button: {
      title: 'Button',
      icon: '⬜',
      defaults: { w: 110, h: 32, props: { text: 'Button' } },
      style: { ...FIELD_STYLE, padding: '5px 12px', cursor: 'pointer' },
      properties: [TEXT('text', 'Text')],
      render: (c) => h('button', { class: 'gfd-button', type: 'button', tabindex: '-1' },
        c.props.text ?? ''),
    },
    // The one control that needs no binding at all: it moves the component through the rows its source
    // returned, so its subject is the component's own collection and there is nothing to point it at.
    // Being a control rather than a fixed bar is the point — it goes wherever the component wants it.
    pager: {
      title: 'Pager',
      icon: '⇄',
      defaults: { w: 200, h: 30, props: { edges: true, position: true } },
      style: {
        display: 'flex',
        'align-items': 'center',
        'justify-content': 'center',
        gap: '4px',
        color: 'var(--text)',
      },
      properties: [BOOL('edges', 'First and last'), BOOL('position', 'Show position')],
      dataNote: 'A pager follows the component\'s own source: it shows where you are in the rows and '
        + 'moves between them. There is nothing to bind.',
      render: (c, context) => {
        const { rowIndex, rowCount } = context;
        const step = (label, title, to, disabled) => h('button', {
          class: 'gfd-pager-btn',
          type: 'button',
          tabindex: '-1',
          title,
          disabled: disabled ? '' : null,
          onclick: () => context.goTo(to),
        }, label);

        const first = rowIndex <= 0 || !rowCount;
        const last = rowIndex >= rowCount - 1 || !rowCount;
        return h('div', { class: 'gfd-pager' },
          c.props.edges ? step('«', 'First record', 0, first) : null,
          step('‹', 'Previous record', rowIndex - 1, first),
          c.props.position
            ? h('span', {
              class: 'gfd-pager-position',
              text: rowCount ? `${rowIndex + 1} of ${rowCount}` : 'No records',
            })
            : null,
          step('›', 'Next record', rowIndex + 1, last),
          c.props.edges ? step('»', 'Last record', rowCount - 1, last) : null);
      },
    },
    panel: {
      title: 'Panel',
      icon: '▦',
      container: true,
      defaults: { w: 320, h: 200, props: { title: '' } },
      style: {
        border: '1px solid var(--border)',
        'border-radius': '6px',
        background: 'color-mix(in srgb, var(--panel-2) 60%, transparent)',
      },
      properties: [TEXT('title', 'Title')],
      render: (c) => h('div', { class: 'gfd-panel' },
        c.props.title ? h('div', { class: 'gfd-panel-title', text: c.props.title }) : null),
    },
  };

  // Which property a control's displayed value lives in. `value` is the slot for controls whose
  // value is not a property of their own, and is what `bind` writes into the rendered element.
  const valueKeyOf = (spec) => spec.valueKey || 'value';

  // A kind's default appearance is usually fixed, but a control that changes shape changes with
  // it, so a kind may work its style out from the control instead of stating one.
  const styleOf = (spec, control) =>
    (typeof spec.style === 'function' ? spec.style(control) : spec.style) || {};

  // ---- document ---------------------------------------------------------------

  const newId = () => 'c' + Math.random().toString(36).slice(2, 10);

  function newDocument() {
    return {
      layout: 'free',
      width: 720,
      height: 460,
      css: '',
      // A component is a fixed canvas by default: controls placed at coordinates do not reflow, so
      // clipping is the honest behaviour until the operator asks for scrollbars.
      showScrollbars: false,
      resizable: false,
      // Off by default: a component designed inside the workspace usually wants to look like it. Turn
      // it on to start from the browser's own styling instead of Gridlet's.
      isolated: false,
      source: null,
      elementId: '',
      classes: '',
      tip: '',
      // The JavaScript modules this component runs, by name. Names only: the code lives in its own
      // files, so the document stays a description of the component rather than a container for source.
      modules: [],
      controls: [],
    };
  }

  // Properties worth reporting for a browser default. A full computed style is hundreds of
  // entries, almost all of them noise; these are the ones that decide how a control looks.
  const BROWSER_DEFAULT_PROPERTIES = [
    'display', 'box-sizing', 'font-family', 'font-size', 'font-weight', 'line-height',
    'color', 'background-color', 'border-width', 'border-style', 'border-color',
    'border-radius', 'padding', 'margin', 'text-align', 'cursor', 'appearance',
  ];

  // The browser's own styling for an element, read from a document that has no author CSS in it
  // at all. An empty same-origin iframe is the only honest place to measure that: anywhere in the
  // workspace, Gridlet's stylesheet is already applying.
  let probeFrame = null;

  function browserDefaultsFor(element) {
    try {
      if (!probeFrame || !probeFrame.isConnected) {
        probeFrame = h('iframe', { 'aria-hidden': 'true', tabindex: '-1', class: 'gfd-probe' });
        document.body.append(probeFrame);
      }

      const probeDocument = probeFrame.contentDocument;
      if (!probeDocument?.body) return [];

      const clone = element.cloneNode(true);
      clone.removeAttribute('class');
      probeDocument.body.append(clone);
      const computed = probeFrame.contentWindow.getComputedStyle(clone);
      const defaults = BROWSER_DEFAULT_PROPERTIES
        .map((property) => [property, computed.getPropertyValue(property)])
        .filter(([, value]) => value && value !== 'none' && value !== 'normal' && value !== 'auto');
      clone.remove();
      return defaults;
    } catch {
      return [];
    }
  }

  const newColors = () => ({ light: { text: '', background: '' }, dark: { text: '', background: '' } });

  // A colour pair becomes one declaration. light-dark() resolves against the element's colour
  // scheme, which the component already sets, so a single generated rule serves both themes and stays
  // readable. The side that was left empty falls back to whatever would have applied anyway.
  //
  // Both sides have to be colours. light-dark() takes two <color> values and nothing else, so a
  // CSS-wide keyword such as `inherit` makes the whole declaration invalid and the browser throws
  // it away — the colour you picked for one theme silently doing nothing. `currentColor` is a real
  // colour that means the same thing here, so callers pass that, or a variable, and never a keyword.
  function themedColor(colors, key, fallback) {
    const light = colors?.light?.[key]?.trim();
    const dark = colors?.dark?.[key]?.trim();
    if (!light && !dark) return null;
    return `light-dark(${light || fallback}, ${dark || fallback})`;
  }

  function readStored(key, fallback) {
    try { return localStorage.getItem(key) ?? fallback; } catch { return fallback; }
  }


  // Published endpoints answer GET with {"rows":[…],"rowCount":n}. Only GET endpoints can back a
  // read-only component; the others change data, and reading a component must never do that.
  async function readSource(source) {
    if (!source?.route) return [];
    const segment = state.meta?.publishedApiSegment || 'pub';
    const query = Object.entries(source.parameters || {})
      .filter(([, value]) => value !== '' && value !== null && value !== undefined)
      .map(([name, value]) => `${encodeURIComponent(name)}=${encodeURIComponent(value)}`)
      .join('&');
    const body = await api(`${segment}/${source.route}${query ? '?' + query : ''}`);
    return Array.isArray(body?.rows) ? body.rows : [];
  }

  function newControl(type, x, y) {
    const spec = CATALOGUE[type];
    const control = {
      id: newId(),
      type,
      name: '',
      x, y,
      w: spec.defaults.w,
      h: spec.defaults.h,
      props: { ...spec.defaults.props },
      // Colours are properties rather than something to hand-write, and each is held per theme,
      // because a colour that works on a dark component rarely works on a light one. Empty means
      // "leave the default alone" for that theme.
      colors: newColors(),
      // Expressions, by property name. A property is either a literal above or an expression here,
      // never both at once, so what a control is showing has exactly one source.
      bind: {},
      // CSS rules kept with this control.
      css: '',
      // What the control is called in HTML, for the operator's own CSS and, later, scripting.
      // Separate from `name`, which is the document's identity for it, and from `id`, which is
      // the designer's and never leaves this file.
      elementId: '',
      classes: '',
      // The tooltip the person filling the component in sees when the pointer rests on this control.
      // It is the HTML title, so it is theirs to write and the browser's to show.
      tip: '',
    };
    if (spec.container) control.controls = [];
    return control;
  }

  // Names are how script and bindings will refer to a control, so they are unique per document
  // from the start rather than becoming unique later, when components already exist.
  function uniqueName(doc, type) {
    const taken = new Set();
    walk(doc.controls, (c) => taken.add((c.name || '').toLowerCase()));
    for (let i = 1; ; i++) {
      const candidate = type + i;
      if (!taken.has(candidate)) return candidate;
    }
  }

  function walk(controls, visit, parent = null) {
    for (const control of controls) {
      visit(control, parent);
      if (control.controls) walk(control.controls, visit, control);
    }
  }

  function findControl(doc, id) {
    let found = null;
    walk(doc.controls, (c) => { if (c.id === id) found = c; });
    return found;
  }

  function findParentList(doc, id) {
    let list = null;
    const scan = (controls) => {
      for (const control of controls) {
        if (control.id === id) { list = controls; return; }
        if (control.controls) scan(control.controls);
      }
    };
    scan(doc.controls);
    return list;
  }

  // The placement grid and its snapping are aids, not rules: a layout that has to sit between two
  // grid lines needs them off, and seeing the lines is a separate question from being held to
  // them. Both are workspace preferences rather than part of a component, so they outlive the tab
  // and never travel to whoever opens the component next.
  let showGrid = readStored('gridlet.components.grid', '1') !== '0';
  let snapToGrid = readStored('gridlet.components.snap', '1') !== '0';

  const snap = (value) => (snapToGrid ? Math.round(value / GRID) * GRID : Math.round(value));

  // ---- designer tab -----------------------------------------------------------

  function openDesigner(component) {
    const key = 'component-designer:' + (component?.id || 'new:' + newId());
    openTab({
      key,
      badge: 'C',
      title: component?.name || 'New component',
      // The designer cannot draw a component until it can evaluate one, and evaluating one needs the
      // functions from gridlet.js. In practice that arrived while the workspace was starting.
      render: async (panel, tab) => {
        await standardLibrary;
        buildDesigner(panel, tab, component);
      },
      // Only a saved component can be rebuilt from its id. An unsaved one has nothing on the server to
      // come back from, so it is deliberately not restored rather than restored empty.
      restore: component?.id ? { kind: 'component', id: component.id } : null,
    });
  }

  registerTabRestorer('component', async (descriptor) => {
    const component = await api('api/components/' + encodeURIComponent(descriptor.id));
    openDesigner(component);
  });

  function buildDesigner(panel, tab, saved) {
    const doc = saved ? structuredClone(saved.definition) : newDocument();
    const model = {
      id: saved?.id || null,
      name: saved?.name || 'New component',
      doc,
      // What is selected, in the order it was picked. The first is the one the panel reads its
      // values from; edits made there are pushed to the rest, and geometry works on the box the
      // whole selection occupies.
      selection: [],
      // Design lays the component out; Preview runs it as the person filling it in will see it. The
      // same renderer draws both, so preview cannot drift from what was designed.
      mode: 'design',
      // GET endpoints available to bind to, the column names the chosen one returns, and the rows
      // preview is showing. Columns are learned by reading the source once, so the field pickers
      // offer real names instead of asking the operator to remember them.
      endpoints: [],
      columns: [],
      rows: [],
      rowIndex: 0,
      // Data was a page of its own and is now part of Settings, so a browser that remembers it
      // opens the page its contents moved to rather than nothing at all.
      tab: TABS.some((page) => page.id === readStored('gridlet.components.tab', 'settings'))
        ? readStored('gridlet.components.tab', 'settings')
        : 'settings',
      // The modules the workspace holds. They belong to the workspace rather than to one component —
      // the component names the ones it runs, and any component can run any of them — so the designer only
      // keeps the list to offer, and each module is edited in its own tab.
      scripts: [],
    };

    // Older documents predate these fields; fill them in rather than special-casing every read.
    doc.showScrollbars ??= false;
    doc.resizable ??= false;
    doc.isolated ??= false;
    doc.source ??= null;
    doc.elementId ??= '';
    doc.classes ??= '';
    doc.tip ??= '';
    doc.modules ??= [];
    doc.colors ??= newColors();
    doc.bind ??= {};
    walk(doc.controls, (control) => {
      control.css ??= '';
      control.elementId ??= '';
      control.classes ??= '';
      control.tip ??= '';
      control.bind ??= {};
      const valueKey = valueKeyOf(CATALOGUE[control.type]);
      // A bound control used to name a column and nothing else. That is one expression among many
      // now, so the column becomes the expression it always meant, on whichever property is this
      // control's value.
      if (control.field) {
        control.bind[valueKey] ??= /^[A-Za-z_][A-Za-z0-9_]*$/.test(control.field)
          ? `data.${control.field}`
          : `data[${JSON.stringify(control.field)}]`;
        delete control.field;
      }
      // A value that used to sit in the generic slot moves onto the property it really drives, so
      // a label's caption and its value stop being two settings fighting over the same text.
      if (valueKey !== 'value' && typeof control.bind.value === 'string') {
        control.bind[valueKey] ??= control.bind.value;
        delete control.bind.value;
      }
      // A control's box used to take a bare declaration list. It takes rules now, like the component's,
      // so an older document's declarations are turned into the rule they were standing in for —
      // visibly, in the box, rather than by a shim that keeps the two boxes subtly different.
      if (control.css.trim() && !control.css.includes('{')) {
        // Both halves of the control, because a bare list meant "this control" without saying
        // which element. `data-name` is the element you see; the box is named beside it.
        const box = control.name ? `[data-control-box="${control.name}"]` : `[data-id="${control.id}"]`;
        const element = control.name ? `[data-name="${control.name}"]` : `[data-id="${control.id}"] > :first-child`;
        control.css = `${box},\n${element} {\n${control.css.trim()}\n}`;
      }
      // Colours used to be one value for both themes; carry an older document's choice into both.
      if (!control.colors) {
        control.colors = newColors();
        for (const theme of ['light', 'dark']) {
          control.colors[theme].text = control.color || '';
          control.colors[theme].background = control.background || '';
        }
      }
      delete control.color;
      delete control.background;
    });

    // A binding used to be an expression on its own, told apart from a fixed value by living in a
    // separate field and by a ƒ button in the panel. A formula says so itself now, the way a
    // spreadsheet cell does, so an older document's expressions are given the `=` they always
    // meant. The event handlers a control carries are formulas too, and they are kept apart from
    // its properties because a property is worked out on every draw and a handler must not be.
    const migrateFormulas = (target) => {
      target.bind ??= {};
      target.events ??= {};
      for (const [key, value] of Object.entries(target.bind)) {
        if (typeof value === 'string' && value.trim() && !isFormula(value)) {
          target.bind[key] = FORMULA + value;
        }
      }
    };
    migrateFormulas(doc);
    walk(doc.controls, migrateFormulas);

    const canvas = h('div', { class: 'gfd-canvas', tabindex: '0' });
    const surface = h('div', { class: 'gfd-surface' }, canvas);
    const propertyBody = h('div', { class: 'gfd-properties-body' });
    // Order matters: each control's default appearance first, then the designer's generated rules,
    // then the operator's custom rules, so equal-specificity custom rules win by document order.
    // The defaults are also in a cascade layer, which is what lets a plain class beat them without
    // having to out-specify a selector the designer wrote.
    const defaultStyle = h('style');
    const generatedStyle = h('style');
    const customStyle = h('style');

    const markDirty = () => {
      // The tab bar shows the unsaved mark, so it is redrawn the once the component becomes dirty and
      // not on every keystroke afterwards.
      if (!tab.hasUnsavedDefinition) {
        tab.hasUnsavedDefinition = true;
        refreshTabs();
      }
      saveButton.disabled = false;
    };

    const saveButton = h('button', {
      class: 'primary',
      'data-testid': 'component-save',
      onclick: () => saveComponent(),
    }, 'Save');

    // ---- resolving properties ----
    // Nothing reads a property straight off the document, because a property may be an expression.
    // One pass resolves the whole component and is thrown away afterwards, so a control that follows
    // another one always sees where that control is now rather than where it was drawn.

    // The colour slots are addressed as one key each, so a colour binds like any other property.
    const COLOUR_KEYS = {
      'color.light': ['light', 'text'],
      'color.dark': ['dark', 'text'],
      'fill.light': ['light', 'background'],
      'fill.dark': ['dark', 'background'],
    };

    // Which fields belong to a control or component itself rather than to its catalogue properties.
    // Listed rather than probed, so a control kind can have a property called `width` without the
    // panel reading or writing the wrong one.
    const OWN_KEYS = new Set([
      'x', 'y', 'w', 'h', 'width', 'height', 'classes', 'elementId', 'tip',
    ]);

    function literalOf(target, key) {
      if (Object.hasOwn(COLOUR_KEYS, key)) {
        const [theme, slot] = COLOUR_KEYS[key];
        return target.colors?.[theme]?.[slot] ?? '';
      }
      if (key === 'value') return undefined;
      if (OWN_KEYS.has(key)) return target[key];
      return Object.hasOwn(target.props ?? {}, key) ? target.props[key] : undefined;
    }

    // Which properties a control offers to bind, in the order the panel shows them. The generic
    // value slot is listed only for controls that have one; the rest name a property already here.
    function bindableKeys(control) {
      const spec = CATALOGUE[control.type];
      return ['x', 'y', 'w', 'h',
        ...(spec.bindable && valueKeyOf(spec) === 'value' ? ['value'] : []),
        ...spec.properties.map((property) => property.key),
        'classes', 'elementId', 'tip', ...Object.keys(COLOUR_KEYS)];
    }

    function resolveAll() {
      const component = model.doc;
      const byName = new Map();
      walk(component.controls, (control) => {
        if (control.name) byName.set(control.name.toLowerCase(), control);
      });

      const values = new Map();
      const errors = new Map();
      const active = new Set();
      const row = currentRow();
      const idOf = (target) => (target === component ? '@component' : target.id);

      function resolve(target, key) {
        const cacheKey = idOf(target) + ':' + key;
        if (values.has(cacheKey)) return values.get(cacheKey);
        const stored = target.bind?.[key];
        // A property with no formula is its own value, and nothing is evaluated at all.
        if (!isFormula(stored)) return literalOf(target, key);
        const expression = formulaBody(stored).trim();
        if (!expression) return literalOf(target, key);
        if (active.has(cacheKey)) {
          // Two properties waiting on each other. Whichever is asked for second answers #CIRC!, so
          // the loop ends in a component you can still see and a code you can read.
          errors.set(cacheKey, 'This formula depends on itself.');
          return makeError(ERROR.CIRC, 'This formula depends on itself.');
        }

        active.add(cacheKey);
        let value;
        try {
          value = evaluate(expression, (parts) => lookup(target, parts), expressionScope);
        } catch (err) {
          // Only a formula that cannot be read at all gets this far: everything the evaluator
          // understands comes back as a value, error codes included.
          value = makeError(ERROR.SYNTAX, err.message);
        }
        active.delete(cacheKey);

        if (isError(value)) errors.set(cacheKey, value.detail || value.code);
        else storeLastGood(target, key, value);

        values.set(cacheKey, value);
        return value;
      }

      // The value a property last worked out to, kept beside the formula. It is what a number
      // property falls back to when the formula is broken, and it is what a reader of the saved
      // document sees without having to evaluate anything. It is not an edit, so it does not make
      // the component dirty; the next real save writes it out.
      function storeLastGood(target, key, value) {
        if (typeof value !== 'string' && typeof value !== 'number' && typeof value !== 'boolean') return;
        if (key === 'value') return;
        const numeric = OWN_KEYS.has(key) && key !== 'classes' && key !== 'elementId' && key !== 'tip';
        setLiteral(target, key, numeric ? asNumber(value) : value);
      }

      // Walking the rest of a path into a plain value, which is all a module's own object is.
      const reach = (value, parts) =>
        parts.reduce((step, part) => (step == null ? undefined : step[part]), value);

      function lookup(target, parts) {
        const head = parts[0];
        const rest = parts.slice(1);
        const key = head.toLowerCase();
        if (key === 'data') return dataValue(rest);
        if (key === 'component') return componentValue(rest);
        if (key === 'self') return memberOf(target, rest);
        const named = byName.get(key);
        if (named) return memberOf(named, rest);
        // A value a module exports is a name in scope, the same way a control is. Modules are asked
        // last, so a control never loses its own name to a file the component happens to run.
        const exported = expressionScope.value(head);
        if (exported.error) return exported.error;
        if (exported.found) return reach(exported.value, rest);
        // Qualified, which is how two modules that export the same name are both still readable:
        // tax.VAT_RATE beside rates.VAT_RATE. A control keeps its own name either way, because a
        // control was asked about first.
        if (rest.length) {
          const member = expressionScope.member(head, rest[0]);
          if (member.found) return reach(member.value, rest.slice(1));
        }
        return makeError(ERROR.NAME, `There is nothing called "${head}" in this component.`);
      }

      // Aliases and derived edges, because a coordinate is easier to write against the side of a
      // control than against its origin plus its size.
      const GEOMETRY = {
        x: 'x', left: 'x', y: 'y', top: 'y',
        w: 'w', width: 'w', h: 'h', height: 'h',
      };

      function memberOf(control, rest) {
        if (control === component) return componentValue(rest);
        if (!rest.length) return control.name;
        const key = rest[0];
        const lowered = key.toLowerCase();

        // A reference to a property that is in error is itself in error, the way a cell pointing
        // at #VALUE! shows #VALUE!. An edge is two properties, so either one can be the answer.
        const edge = (first, second, combine) => {
          const a = resolve(control, first);
          if (isError(a)) return a;
          if (second === null) return asNumber(a);
          const b = resolve(control, second);
          if (isError(b)) return b;
          return combine(asNumber(a), asNumber(b));
        };

        if (Object.hasOwn(GEOMETRY, lowered)) return edge(GEOMETRY[lowered], null);
        if (lowered === 'right') return edge('x', 'w', (x, w) => x + w);
        if (lowered === 'bottom') return edge('y', 'h', (y, h) => y + h);
        if (lowered === 'centrex' || lowered === 'centerx') return edge('x', 'w', (x, w) => x + w / 2);
        if (lowered === 'centrey' || lowered === 'centery') return edge('y', 'h', (y, h) => y + h / 2);
        if (lowered === 'name') return control.name;
        if (lowered === 'type') return control.type;
        if (lowered === 'value') return resolve(control, 'value');
        // The attributes a control carries read back the same way its geometry does, so anything
        // the panel can set, an expression elsewhere can follow.
        if (lowered === 'tip') return resolve(control, 'tip');
        if (lowered === 'classes') return resolve(control, 'classes');
        if (lowered === 'elementid') return resolve(control, 'elementId');
        const property = CATALOGUE[control.type].properties
          .find((candidate) => candidate.key.toLowerCase() === lowered);
        if (property) return resolve(control, property.key);
        return makeError(ERROR.NAME,
          `"${control.name || control.type}" has no property called "${key}".`);
      }

      function dataValue(rest) {
        // `data` on its own is the whole row. Anything that turns a value into text renders an
        // object as JSON, so a control bound to the row shows the row — which is the quickest way
        // to see what a source actually returns.
        if (!rest.length) return row ?? (model.mode === 'preview' ? null : '[row]');
        const column = rest[0];
        if (!row) {
          // Preview is the component for real: an empty result shows empty. While designing, a column
          // stands in for itself, so a layout reads as a description of what it will display.
          return model.mode === 'preview' ? null : `[${column}]`;
        }
        if (Object.hasOwn(row, column)) return row[column];
        const match = Object.keys(row).find((name) => name.toLowerCase() === column.toLowerCase());
        if (match) return row[match];
        return makeError(ERROR.NAME, `The source has no column called "${column}".`);
      }

      function componentValue(rest) {
        // `component` on its own is the component itself — the same object a module's class is handed. It is
        // there so a function a formula calls can be given what it needs: `=showSize(component)` reads
        // as what it does, and the same function works from any control, in a property or in a
        // handler, without anything being passed to it invisibly.
        //
        // A property is worked out while the component is being drawn. Reading the component there is fine;
        // changing it is asking a question and answering it at the same time, and the next draw
        // will overwrite whatever was written. Save that for a handler.
        if (!rest.length) return componentApi;

        const key = rest[0].toLowerCase();
        const size = (name) => {
          const value = resolve(component, name);
          return isError(value) ? value : asNumber(value);
        };
        if (key === 'width') return size('width');
        if (key === 'height') return size('height');
        if (key === 'name') return model.name;
        if (key === 'rowcount') return model.rows.length;
        if (key === 'row') return model.rows.length ? model.rowIndex + 1 : 0;
        // Every row the source returned, not just the one on screen. Shown as JSON like any other
        // object, so json(component.rows, 2) is the whole result, readable.
        if (key === 'rows') return model.rows;
        return makeError(ERROR.NAME, `The component has no property called "${rest[0] ?? ''}".`);
      }

      // A control with every expression already worked out. Renderers take one of these and never
      // see a binding, so the designer draws a bound control exactly as a published component will.
      // A control cannot sit at position #VALUE! and cannot be half-ticked. Where the shape of a
      // property leaves no room to show an error, the property falls back to the value it last
      // worked out to; the code and its reason are on the property panel, where there is room for
      // them. Text keeps the code, the way a spreadsheet cell does.
      const shaped = (target, key, kind) => {
        const value = resolve(target, key);
        if (!isError(value)) {
          return kind === 'boolean' ? truthy(value) : kind === 'number' ? asNumber(value) : value;
        }
        // On the component an error is the code, the same characters a spreadsheet cell shows. The error
        // itself travels only between formulas, which read the property through resolve.
        if (kind === 'text') return value.code;
        const previous = literalOf(target, key);
        return kind === 'boolean' ? truthy(previous) : asNumber(previous);
      };

      function viewOf(control) {
        // A property keeps the shape its kind promises whatever the formula returned, so a check
        // box bound to the text "false" is unchecked rather than mysteriously ticked.
        const props = { ...control.props };
        for (const property of CATALOGUE[control.type].properties) {
          props[property.key] = shaped(control, property.key, property.kind || 'text');
        }
        const colours = { light: {}, dark: {} };
        for (const [key, [theme, slot]] of Object.entries(COLOUR_KEYS)) {
          colours[theme][slot] = asText(resolve(control, key));
        }
        return {
          ...control,
          x: shaped(control, 'x', 'number'),
          y: shaped(control, 'y', 'number'),
          w: shaped(control, 'w', 'number'),
          h: shaped(control, 'h', 'number'),
          props,
          colors: colours,
          classes: asText(resolve(control, 'classes')),
          elementId: asText(resolve(control, 'elementId')),
          tip: asText(resolve(control, 'tip')),
          value: shaped(control, 'value', 'text'),
        };
      }

      function componentView() {
        const colours = { light: {}, dark: {} };
        for (const [key, [theme, slot]] of Object.entries(COLOUR_KEYS)) {
          colours[theme][slot] = asText(resolve(component, key));
        }
        return {
          width: shaped(component, 'width', 'number'),
          height: shaped(component, 'height', 'number'),
          colors: colours,
          classes: asText(resolve(component, 'classes')),
          elementId: asText(resolve(component, 'elementId')),
          tip: asText(resolve(component, 'tip')),
        };
      }

      return {
        viewOf,
        componentView,
        read: resolve,
        // What a formula written on a control can name, for anything evaluated outside a property
        // — an event handler reaches the same row, the same controls and the same component.
        lookupFor: (target) => (parts) => lookup(target, parts),
        // The panel asks about one property at a time, and the pass it is asking has not
        // necessarily worked that property out yet — the canvas and the panel each build their own.
        // Resolving first fills the map before it is read; the result is cached either way.
        errorFor: (target, key) => {
          resolve(target, key);
          return errors.get(idOf(target) + ':' + key) || null;
        },
      };
    }

    // The pass in force for the drawing happening now. Rebuilt wherever the sheet is rebuilt, so a
    // property that follows another is never a frame behind it.
    let pass = null;

    // ---- rendering ----

    function renderCanvas() {
      // The designer's own rules are a real stylesheet, regenerated from the document on every
      // render. It loads before the component's custom CSS, so custom rules of equal specificity win
      // by order, and either the property or the variable behind it can be overridden.
      applyStyles();
      const component = pass.componentView();
      canvas.className = ['gfd-canvas', model.mode === 'preview' ? 'preview' : '',
        showGrid ? '' : 'no-grid',
        component.classes || ''].filter(Boolean).join(' ').trim();
      canvas.id = component.elementId || '';
      canvas.title = component.tip || '';
      canvas.dataset.component = model.name || 'component';
      // Controls placed beyond the component's edge are clipped rather than spilling onto the page.
      // Scrollbars are how the operator opts into reaching them instead.
      canvas.style.overflow = model.mode !== 'preview' ? 'visible'
        : model.doc.showScrollbars ? 'auto' : 'hidden';
      canvas.style.resize = model.mode === 'preview' && model.doc.resizable ? 'both' : '';
      canvas.replaceChildren(...model.doc.controls.map(renderControl));
    }

    const currentRow = () => model.rows[model.rowIndex] || null;

    function renderControl(control) {
      const spec = CATALOGUE[control.type];
      const selected = model.mode === 'design' && isSelected(control.id);
      const view = pass.viewOf(control);
      const valueKey = valueKeyOf(spec);
      const bound = Boolean(control.bind?.[valueKey]?.trim());
      // Where the component is in its rows, for the kinds whose whole job is that. Passing it in keeps
      // the renderers pure functions of what they are given, the same as every other control.
      const inner = spec.render(view, {
        mode: model.mode,
        rowIndex: model.rowIndex,
        rowCount: model.rows.length,
        goTo: (index) => showRow(index),
      });
      // In preview the tip is the whole tooltip: the component is the component, and the designer has no
      // business talking over it. While designing, the control's name and what drives it are worth
      // having, so the tip goes above them rather than instead of them.
      const hint = bound ? `${control.name} = ${control.bind[valueKey]}` : control.name;

      // The name, the class and the id all go on the element you can see, not on the box around
      // it. "button1" means the button to whoever named it; the box is the designer's, and it
      // exists to position the control, hold its handles and take the click that selects it.
      // `[data-name="button1"] { border: 1px solid red }` therefore puts a border on the button,
      // and an id lands where a `<label for=…>` can point at it.
      //
      // The box carries the same name under its own attribute, which is what the geometry rule
      // targets. One name, two attributes, so a rule can never mean both elements at once.
      if (control.name) inner.dataset.name = control.name;
      if (view.classes.trim()) inner.classList.add(...view.classes.trim().split(/\s+/));
      if (view.elementId) inner.id = view.elementId;

      const element = h('div', {
        class: 'gfd-control' + (selected ? ' selected' : '')
          + (bound ? ' bound' : ''),
        'data-id': control.id,
        'data-control-box': control.name || null,
        'data-type': control.type,
        title: model.mode === 'preview'
          ? (view.tip || null)
          : [view.tip, hint].filter(Boolean).join('\n') || null,
      }, inner);

      // The value is resolved the same way in both views. Design shows a column as its own name,
      // so a laid-out component reads as a description of what it will display rather than as a row of
      // empty boxes, and preview shows what the row actually holds. A control whose value is one
      // of its own properties is already drawn from it — only the generic slot needs putting in.
      if (bound && valueKey === 'value' && spec.bind) spec.bind(inner, view.value);
      // No inline geometry: it goes into the generated stylesheet instead. Inline custom
      // properties beat every stylesheet rule, so writing them here would make the component's own CSS
      // unable to redefine the variables — which is the whole point of exposing them.

      if (spec.container) {
        const inner = h('div', { class: 'gfd-container' },
          ...(control.controls || []).map(renderControl));
        element.append(inner);
      }

      // Controls are drawn out of the tab order while designing, because tabbing belongs to the
      // designer's own chrome. Preview puts them back in it, so the component can be filled in by
      // keyboard exactly as it will be for real.
      if (model.mode === 'preview') {
        for (const focusable of element.querySelectorAll('[tabindex="-1"]')) {
          focusable.removeAttribute('tabindex');
        }
      }

      // Handles resize one control. With several selected the size fields on the Appearance page
      // are the way to resize them, so the handles stay out of a drag that would be ambiguous.
      if (selected && model.selection.length === 1) {
        for (const handle of ['e', 's', 'se']) {
          element.append(h('div', { class: 'gfd-handle gfd-handle-' + handle, 'data-handle': handle }));
        }
      }

      return element;
    }

    // Custom CSS is the operator's, but it is written against one component. Prefixing every rule with
    // the canvas's own attribute keeps a component's styles off the rest of the workspace.
    function scopeCss(css, root) {
      if (!css || !css.trim()) return '';
      if (!root.dataset.gfdScope) root.dataset.gfdScope = newId();
      const scope = `[data-gfd-scope="${root.dataset.gfdScope}"]`;
      return css.replace(/(^|\})\s*([^@{}]+)\{/g, (match, brace, selectors) => {
        const scoped = selectors.split(',')
          .map((s) => `${scope} ${s.trim()}`)
          .join(', ');
        return `${brace} ${scoped} {`;
      });
    }

    // ---- selection and properties ----

    // The controls that are selected, in the order they were picked and skipping any that have
    // since been deleted. The first is the primary: the one the panel shows.
    function selectedControls() {
      return model.selection
        .map((id) => findControl(model.doc, id))
        .filter(Boolean);
    }

    const isSelected = (id) => model.selection.includes(id);

    // `add` toggles one control in or out of the selection instead of replacing it, which is what
    // a modifier-click asks for. Selecting nothing selects the component.
    function select(id, add = false) {
      if (id === null) model.selection = [];
      else if (!add) model.selection = [id];
      else if (isSelected(id)) model.selection = model.selection.filter((other) => other !== id);
      else model.selection = [...model.selection, id];
      renderCanvas();
      renderProperties();
    }

    function selectAll(ids, add = false) {
      const kept = add ? model.selection.filter((id) => !ids.includes(id)) : [];
      model.selection = [...kept, ...ids];
      renderCanvas();
      renderProperties();
    }

    function renderProperties() {
      generatedView = null;
      cascadeRefresh = null;
      expressionChecks = [];
      // The panel is rebuilt from scratch, so the boxes it held are gone. The tabs are not: they
      // outlive any one selection.
      cssBoxes.clear();
      pass = resolveAll();
      const controls = selectedControls();
      const control = controls[0] || null;
      // A selection of several is a subject in its own right: it says how many, and which, because
      // "3 controls" alone leaves you checking the canvas to find out what you are about to change.
      if (controls.length > 1) {
        subjectLabel.textContent = `${controls.length} controls`;
        subjectName.textContent = controls.map((c) => c.name || c.type).join(', ');
      } else {
        subjectLabel.textContent = control ? CATALOGUE[control.type].title : 'Component';
        subjectName.textContent = control ? control.name : model.name;
      }
      subjectName.title = subjectName.textContent;
      for (const [id, button] of tabButtons) {
        const active = id === model.tab;
        button.classList.toggle('active', active);
        button.setAttribute('aria-selected', String(active));
      }
      propertyBody.replaceChildren(referenceList(), ...(control
        ? controlEditors(control, model.tab)
        : componentEditors(model.tab)).filter(Boolean));
      if (pendingFocus) {
        propertyBody.querySelector(`[data-bind-key="${pendingFocus}"]`)?.focus();
        pendingFocus = null;
      }
    }

    // A collapsible block in the panel. Open state is remembered by key, so a section someone
    // keeps closed stays closed across selections and reloads.
    function section(key, title, ...children) {
      const open = readStored('gridlet.components.section.' + key, '0') === '1';
      const details = h('details', open ? { class: 'gfd-section', open: '' } : { class: 'gfd-section' },
        h('summary', { class: 'gfd-heading', text: title }),
        ...children);
      details.addEventListener('toggle', () => {
        try { localStorage.setItem('gridlet.components.section.' + key, details.open ? '1' : '0'); }
        catch { /* unavailable */ }
      });
      return details;
    }

    const heading = (text) => h('div', { class: 'gfd-heading', text });
    const note = (text) => h('p', { class: 'field-note gfd-note', text });

    // A textarea has no `value` content attribute — its text is its content — so the initial text
    // is assigned after creation. Setting it as an attribute renders an empty box holding CSS that
    // is still being applied, which reads as the styling having been lost.
    function textArea(value, rows, className, onChange) {
      const element = h('textarea', {
        class: className,
        rows,
        spellcheck: 'false',
        oninput: (event) => onChange(event.target.value),
      });
      element.value = value ?? '';
      return element;
    }

    // ---- custom CSS ----
    // The component has a box for its own rules and every control has one for its own. Both hold the
    // same thing — CSS, scoped to this component — and both can be opened in a tab of their own when
    // six rows in a panel stop being enough room. The box and the tab are two views of one string:
    // whichever is not being typed in follows the one that is, so there is never a newer copy
    // somewhere else.

    const cssTargetId = (target) => (target === model.doc ? '@component' : target.id);
    const cssTargetName = (target) =>
      (target === model.doc ? model.name || 'component' : target.name || target.type);

    const cssBoxes = new Map();
    const cssTabs = new Map();

    function cssChanged(target, value, from) {
      target.css = value;
      applyStyles();
      markDirty();

      const id = cssTargetId(target);
      const box = cssBoxes.get(id);
      if (from !== 'panel' && box && box !== document.activeElement) box.value = value;
      const editor = cssTabs.get(id);
      if (from !== 'tab' && editor && editor.input !== document.activeElement) {
        editor.input.value = value;
        editor.refresh();
      }
    }

    function cssEditor(target, placeholder = null) {
      const element = textArea(target.css, '6', 'gfd-css',
        (next) => cssChanged(target, next, 'panel'));
      // The placeholder carries the selector this box is usually for, so the shape of a rule is
      // visible without having to be explained.
      if (placeholder) element.placeholder = placeholder;
      cssBoxes.set(cssTargetId(target), element);
      return element;
    }

    // The box, and the button that gives it a whole tab. Written once because the component's rules and
    // a control's rules are the same thing about different subjects.
    function cssSection(key, target, placeholder) {
      return section(key, 'Custom CSS',
        h('div', { class: 'gfd-css-head' },
          h('button', {
            class: 'ghost gfd-css-expand',
            type: 'button',
            'data-testid': 'css-expand-' + cssTargetId(target),
            title: `Edit ${cssTargetName(target)}'s CSS in its own tab`,
            onclick: () => openCssTab(target),
          }, 'Open in a tab')),
        cssEditor(target, placeholder));
    }

    function openCssTab(target) {
      const id = cssTargetId(target);
      openTab({
        key: `component-css:${model.id || 'unsaved-' + tab.id}:${id}`,
        badge: 'CSS',
        title: `${cssTargetName(target)} CSS`,
        render: (panel, cssTab) => buildCssTab(panel, cssTab, target),
        // Only a saved component can be found again by id, so only a saved component's stylesheet comes back.
        restore: model.id ? { kind: 'component-css', id: model.id, target: id } : null,
      });
    }

    // Every selector this component really has, so what completes in a selector is what is actually
    // there to style rather than a list of what CSS allows. A control is two elements — the box
    // the designer positions and the element you see — and both are offered, because a rule about
    // where something sits and a rule about what it looks like belong to different halves.
    function componentSelectors(subject = null) {
      const found = [
        { text: componentSelector(), detail: 'the component' },
        { text: '.gfd-canvas', detail: 'the component surface' },
      ];
      walk(model.doc.controls, (control) => {
        const view = pass.viewOf(control);
        if (control.name) {
          found.push({ text: innerSelectorFor(control), detail: `${control.type} ${control.name}` });
          found.push({ text: selectorFor(control), detail: `the box around ${control.name}` });
        }
        for (const className of (view.classes || '').trim().split(/\s+/).filter(Boolean)) {
          found.push({ text: '.' + className, detail: `class on ${control.name || control.type}` });
        }
        if (view.elementId) {
          found.push({ text: '#' + view.elementId, detail: `id of ${control.name || control.type}` });
        }
      });
      // A control's own stylesheet is about that control, so its own selectors come first. The
      // rest of the component is still there: a rule written here is scoped to the component, not to the
      // control, and styling a neighbour from here is allowed even if it is rarely what is meant.
      if (!subject) return found;
      const mine = (entry) => entry.detail.endsWith(subject) || entry.text.includes(`"${subject}"`);
      return [...found.filter(mine), ...found.filter((entry) => !mine(entry))];
    }

    // The custom properties in reach: the ones the designer writes on every control, and any this
    // component's own rules define.
    function cssVariables() {
      const found = new Set(GRIDLET_VARIABLES);
      for (const [, name] of customCssSource().matchAll(/(--[A-Za-z0-9_-]+)\s*:/g)) found.add(name);
      return [...found];
    }

    // Which part of a rule the caret is in. Braces are counted after comments and strings are
    // taken out, which is as much parsing as deciding what to offer needs.
    function cssContext(text, caret) {
      const before = text.slice(0, caret);
      const stripped = before
        .replace(/\/\*[\s\S]*?\*\//g, '')
        .replace(/"(?:\\.|[^"\\])*"/g, '""')
        .replace(/'(?:\\.|[^'\\])*'/g, "''");
      const depth = (stripped.match(/\{/g) || []).length - (stripped.match(/\}/g) || []).length;
      const wordOf = (pattern) => (before.match(pattern) || [''])[0];

      if (depth <= 0) return { kind: 'selector', word: wordOf(/[\w.#[\]"'=:-]*$/) };

      const segment = stripped.slice(Math.max(
        stripped.lastIndexOf('{'), stripped.lastIndexOf(';'), stripped.lastIndexOf('}')) + 1);
      const colon = segment.indexOf(':');
      if (colon < 0) return { kind: 'property', word: wordOf(/[\w-]*$/) };
      return {
        kind: 'value',
        property: segment.slice(0, colon).trim(),
        word: wordOf(/[\w#%.()-]*$/),
      };
    }

    // What could come next. Named things this component holds come before things CSS merely allows,
    // because the first kind is the reason to have the list at all.
    function cssSuggestions(text, caret, subject = null) {
      const context = cssContext(text, caret);
      const word = context.word || '';
      const lowered = word.toLowerCase();
      const matches = (candidate) => !lowered || candidate.toLowerCase().startsWith(lowered)
        || candidate.toLowerCase().includes(lowered);
      const item = (label, insert, detail, thenSuggest = false) =>
        ({ label, insert, detail, replace: word, thenSuggest });

      if (context.kind === 'selector') {
        const offered = [];
        if (lowered.startsWith(':') || lowered.includes(':')) {
          // A pseudo-class attaches to what is already written, so only the colon onwards is
          // replaced and the selector in front of it is left alone.
          const at = word.lastIndexOf(':');
          const tail = word.slice(at);
          for (const pseudo of CSS_PSEUDOS) {
            if (!pseudo.toLowerCase().startsWith(tail.toLowerCase())) continue;
            offered.push({ label: pseudo, insert: pseudo, detail: 'state', replace: tail });
          }
          if (offered.length) return offered;
        }
        for (const { text: selector, detail } of componentSelectors(subject)) {
          if (matches(selector)) offered.push(item(selector, selector, detail));
        }
        return offered;
      }

      if (context.kind === 'property') {
        const offered = cssVariables()
          .filter(matches)
          .map((name) => item(name, name + ': ', 'variable', true));
        for (const property of CSS_PROPERTIES) {
          if (matches(property)) offered.push(item(property, property + ': ', 'property', true));
        }
        return offered;
      }

      const values = [
        ...(CSS_VALUES[context.property] || []).map((value) => [value, 'value']),
        ...cssVariables().map((name) => [`var(${name})`, 'variable']),
        ...CSS_GLOBALS.map((value) => [value, 'any property']),
      ];
      return values.filter(([value]) => matches(value))
        .map(([value, detail]) => item(value, value, detail));
    }

    function buildCssTab(panel, cssTab, target) {
      const id = cssTargetId(target);
      const { surface, input, highlight, refresh } = codeSurface({
        paint: highlightCss,
        label: `${cssTargetName(target)} CSS`,
        testId: 'component-css-editor',
        onInput: () => cssChanged(target, input.value, 'tab'),
      });

      // The control this stylesheet is about, so its own selectors are offered first.
      const subject = target === model.doc ? null : target.name;
      surface.append(attachCompletions(input, highlight,
        (text, caret) => cssSuggestions(text, caret, subject)));
      input.dataset.cssTarget = id;

      const subtitle = target === model.doc
        ? `${model.name || 'component'} — the component's own rules`
        : `${cssTargetName(target)} — this control's rules`;

      panel.append(h('div', { class: 'gfd-code-tab' },
        h('div', { class: 'viewbar' },
          h('span', { class: 'muted gfd-code-note', text: subtitle }),
          h('span', { class: 'spacer' }),
          h('span', {
            class: 'muted gfd-code-note',
            text: 'Applies as you type. Ctrl+Space suggests. Save the component to keep it.',
          })),
        surface));

      input.value = target.css || '';
      refresh();
      input.focus();

      cssTabs.set(id, { input, refresh, tabId: cssTab.id });
      cssTab.onClose = () => {
        if (cssTabs.get(id)?.tabId === cssTab.id) cssTabs.delete(id);
      };
    }

    // A stylesheet tab edits this component's document, so it goes when the component does. The same when a
    // control is deleted: there is nothing left for its rules to be about.
    function closeCssTabs(targets = null) {
      for (const [id, editor] of [...cssTabs]) {
        if (targets && !targets.includes(id)) continue;
        cssTabs.delete(id);
        closeTab(editor.tabId);
      }
    }

    // ---- property rows ----
    // Every property is one row: what it is called, how it is set, and the switch that swaps the
    // editor for an expression. A coordinate, a colour and a caption all get the same row, so the
    // panel is a table to run an eye down rather than a stack of differently shaped cards.

    function setLiteral(target, key, value) {
      if (Object.hasOwn(COLOUR_KEYS, key)) {
        const [theme, slot] = COLOUR_KEYS[key];
        target.colors[theme][slot] = value;
      } else if (OWN_KEYS.has(key)) target[key] = value;
      else target.props[key] = value;
    }

    const isBound = (target, key) => Boolean(target.bind?.[key]?.trim());

    // ---- one edit, every selected control ----
    // The panel edits the first of the selection and then hands the same decision to the rest.
    // Doing it after the fact rather than through some multi-control stand-in means every editor,
    // and every expression box, works on a group without knowing that groups exist.

    // Whether a control kind has this property at all, so a label in a mixed selection is not
    // given a placeholder it cannot use.
    function hasProperty(control, key) {
      if (OWN_KEYS.has(key) || Object.hasOwn(COLOUR_KEYS, key)) return true;
      if (key === 'value') return valueKeyOf(CATALOGUE[control.type]) === 'value';
      return CATALOGUE[control.type].properties.some((property) => property.key === key);
    }

    // Geometry is not copied: the selection's box owns it, and copying a coordinate would stack
    // every control on top of the first one.
    const SHARED_EXCLUDES = new Set(['x', 'y', 'w', 'h', 'elementId']);

    function broadcast(target, key) {
      if (SHARED_EXCLUDES.has(key)) return;
      for (const control of selectedControls()) {
        if (control === target || !hasProperty(control, key)) continue;
        const expression = target.bind?.[key];
        if (typeof expression === 'string') (control.bind ??= {})[key] = expression;
        else {
          delete control.bind?.[key];
          setLiteral(control, key, literalOf(target, key));
        }
      }
    }

    // Whether the selection disagrees about a property. The panel shows the first control's value,
    // so a row that is not the same everywhere says so rather than quietly implying it is.
    function mixed(target, key) {
      const controls = selectedControls();
      if (controls.length < 2 || SHARED_EXCLUDES.has(key)) return false;
      const expression = target.bind?.[key] ?? null;
      const literal = asText(literalOf(target, key));
      return controls.some((control) => control !== target && hasProperty(control, key)
        && ((control.bind?.[key] ?? null) !== expression
          || (expression === null && asText(literalOf(control, key)) !== literal)));
    }

    const dataReference = (column) => /^[A-Za-z_][A-Za-z0-9_]*$/.test(column)
      ? `data.${column}`
      : `data[${JSON.stringify(column)}]`;

    // What an expression can name, offered as the input's own completion list. It costs one
    // element and no keyboard handling, and it is rebuilt from the document every time the panel
    // is drawn, so it can never offer a control that has been renamed away.
    const REFERENCE_LIST = 'gfd-refs-' + newId();

    function referenceList() {
      // The whole row and the whole result come first: they are the ones nobody guesses are there.
      const references = ['data', 'json(data, 2)', 'json(component.rows, 2)', ...model.columns.map(dataReference)];
      walk(model.doc.controls, (control) => {
        if (!control.name) return;
        for (const key of ['x', 'y', 'w', 'h', 'right', 'bottom', 'tip']) {
          references.push(`${control.name}.${key}`);
        }
        for (const property of CATALOGUE[control.type].properties) {
          references.push(`${control.name}.${property.key}`);
        }
      });
      references.push('component.width', 'component.height', 'component.rowCount', 'component.row',
        'self.w', 'self.h', 'self.x', 'self.y');
      return h('datalist', { id: REFERENCE_LIST },
        references.map((value) => h('option', { value })));
    }

    // Which row's expression box to put the cursor in once the panel has been redrawn. Binding a
    // property is a request to write one, so the box opens ready to type in.
    let pendingFocus = null;

    function row(target, key, label, buildEditor, options = {}) {
      const formula = key !== null && isFormula(target.bind?.[key]);
      const differs = key !== null && mixed(target, key);
      const cell = h('div', { class: 'gfd-cell' });
      cell.append(...[buildEditor()].flat());
      return h('div', {
        class: 'gfd-row' + (formula ? ' bound' : '') + (options.block ? ' block' : '')
          + (differs ? ' mixed' : ''),
        'data-property': key || null,
      },
        h('span', {
          class: 'gfd-row-label',
          title: differs
            ? `${options.hint || label} — the selected controls differ here; this is the first one's`
            : (options.hint || label),
          text: label,
        }),
        cell,
        h('span', { class: 'gfd-bind-gap' }));
    }

    // ---- one box per property ----
    // A property is edited the way a spreadsheet cell is: one box, holding either what the property
    // is or the formula that decides it. There is nothing to switch on first — you type `=` and it
    // is a formula, and `'` in front of anything keeps it as text.
    //
    // What the box shows is what is stored, never what a formula worked out to. Seeing 240 in a box
    // that actually holds `=self.h` is how you lose a formula by typing over it.

    // How a property takes what was typed. A tick box is still a boolean once a formula decides it,
    // and a position is still a number.
    const COERCE = {
      number: (value) => asNumber(value),
      boolean: (value) => truthy(value),
      text: (value) => asText(value),
      lines: (value) => asText(value),
    };

    function writeProperty(target, key, typed, options = {}) {
      const kind = options.kind || 'text';
      if (isFormula(typed)) (target.bind ??= {})[key] = typed;
      else {
        delete target.bind?.[key];
        setLiteral(target, key, COERCE[kind](unescapeText(typed)));
      }
      options.after?.(target);
      broadcast(target, key);
      renderCanvas();
      markDirty();
    }

    // What the box shows for a property that has no formula: the value, with the escape it needs if
    // it happens to start with `=` or `'`.
    const storedText = (target, key, kind) => {
      const stored = target.bind?.[key];
      if (isFormula(stored)) return stored;
      const literal = literalOf(target, key);
      if (kind === 'boolean') return truthy(literal) ? 'true' : 'false';
      if (kind === 'number') return String(asNumber(literal));
      return escapeText(asText(literal));
    };

    // A box that fails is marked and carries the reason. The canvas shows the code; there is only
    // room here for the sentence behind it.
    function watchProperty(target, key, input) {
      const refresh = () => {
        const error = pass.errorFor(target, key);
        input.classList.toggle('bad', Boolean(error));
        input.title = error || input.value || '';
      };
      expressionChecks.push(refresh);
      refresh();
    }

    // A handler box. It takes a formula and nothing else: a handler that is not a formula could
    // never run, so it is marked rather than quietly ignored.
    function eventBox(target, name, hint) {
      const input = h('input', {
        class: 'gfd-expr',
        type: 'text',
        spellcheck: 'false',
        autocomplete: 'off',
        list: REFERENCE_LIST,
        placeholder: '=doSomething(data.Id)',
        title: hint,
        'data-event-key': name,
        'data-testid': 'event-' + name,
        oninput: (event) => {
          const typed = event.target.value.trim();
          if (typed) (target.events ??= {})[name] = typed;
          else delete target.events?.[name];
          mark(event.target);
          markDirty();
        },
      });
      const mark = (element) => {
        const typed = element.value.trim();
        const bad = Boolean(typed) && !isFormula(typed);
        element.classList.toggle('bad', bad);
        element.title = bad ? 'A handler has to start with = to run.' : hint;
      };
      input.value = target.events?.[name] ?? '';
      mark(input);
      return input;
    }

    const eventRows = (target, events) => [
      heading('Events'),
      ...events.map(([name, label, hint]) =>
        row(target, null, label, () => eventBox(target, name, hint), { hint })),
      note('A handler is a formula that is run for what it does. It calls a function one of this '
        + 'component\'s modules exports, and it runs in Preview, not while you are drawing. Pass it '
        + '`component` for something to act on: =showPrice(component, data.Price).'),
    ];

    function propertyBox(target, key, options = {}) {
      const kind = options.kind || 'text';
      const input = h('input', {
        class: 'gfd-expr' + (kind === 'number' ? ' gfd-number' : ''),
        type: 'text',
        inputmode: kind === 'number' ? 'decimal' : null,
        spellcheck: 'false',
        autocomplete: 'off',
        list: REFERENCE_LIST,
        placeholder: options.placeholder || null,
        'data-bind-key': key,
        'data-testid': 'expr-' + key,
        oninput: (event) => writeProperty(target, key, event.target.value, options),
      });
      input.value = storedText(target, key, kind);
      watchProperty(target, key, input);
      return input;
    }

    // ---- editors ----
    // Each one is the same box underneath. What differs is the shape the property keeps when you
    // type a plain value into it, and whether anything sits beside the box to help you choose one.

    const numberEditor = (target, key, options = {}) => () =>
      propertyBox(target, key, { ...options, kind: 'number' });

    const textEditor = (target, key, options = {}) => () =>
      propertyBox(target, key, { ...options, kind: 'text' });

    // The tick is an assist, not the store: it writes `true` or `false` into the box beside it, and
    // the box is what the property reads. A formula therefore fits a tick box like anything else,
    // and the tick shows what the formula worked out to.
    const checkEditor = (target, key, options = {}) => () => {
      const input = propertyBox(target, key, { ...options, kind: 'boolean' });
      const tick = h('input', {
        type: 'checkbox',
        class: 'gfd-check',
        onchange: (event) => {
          input.value = event.target.checked ? 'true' : 'false';
          writeProperty(target, key, input.value, { ...options, kind: 'boolean' });
        },
      });

      // The tick reports what the property came out as, so it follows the box rather than being
      // built once from it: typing a formula into the box is exactly when the tick stops being
      // something to click and starts being something to read.
      const sync = () => {
        const decided = isFormula(target.bind?.[key]);
        tick.checked = truthy(pass.read(target, key));
        tick.disabled = decided;
        tick.title = decided
          ? 'A formula decides this. Clear the formula to tick it by hand.'
          : '';
      };
      expressionChecks.push(sync);
      sync();

      return [tick, input];
    };

    const linesEditor = (target, key, options = {}) => () => {
      const area = textArea(storedText(target, key, 'lines'), '3', 'gfd-lines gfd-expr',
        (next) => writeProperty(target, key, next, { ...options, kind: 'lines' }));
      watchProperty(target, key, area);
      return area;
    };

    // A colour with a way back out of it. A colour input cannot express "not set", so Clear is a
    // separate control rather than a magic value, and the text box takes anything CSS accepts — a
    // variable, a colour function, a hex, or a formula that works one out.
    //
    // The swatch keeps its job when a formula decides the colour: it shows the colour that came
    // out, so the formula and its result are on screen together. A result that is not a colour at
    // all gets a crossed-out swatch instead, because a picker showing black would be a lie.
    const isColour = (value) => Boolean(value)
      && (typeof CSS === 'undefined' || !CSS.supports ? /^#[0-9a-f]{3,8}$/i.test(value)
        : CSS.supports('color', value));
    const isHex = (value) => /^#[0-9a-f]{6}$/i.test(value);

    function colourEditor(target, key, hint) {
      const text = h('input', {
        class: 'gfd-colour-text gfd-expr',
        type: 'text',
        spellcheck: 'false',
        autocomplete: 'off',
        list: REFERENCE_LIST,
        placeholder: 'default',
        'data-bind-key': key,
        'data-testid': 'colour-' + key,
        oninput: (event) => {
          writeProperty(target, key, event.target.value.trim(), { kind: 'text' });
        },
      });
      text.value = storedText(target, key, 'text');
      watchProperty(target, key, text);

      // The swatch says what the colour came out as, so it is rebuilt whenever that changes rather
      // than once when the panel was drawn. It swaps between a picker and a crossed-out box, which
      // is a different element and not an attribute to toggle — so it lives in a slot of its own,
      // and the text box keeps the cursor through all of it.
      const slot = h('span', { class: 'gfd-swatch-slot' });

      const sync = () => {
        const formula = isFormula(target.bind?.[key]);
        const resolved = asText(pass.read(target, key));

        // A crossed-out box rather than a colour input, because there is no colour to put in one.
        slot.replaceChildren(isColour(resolved)
          ? h('input', {
            type: 'color',
            class: 'gfd-swatch',
            'data-testid': 'colour-swatch-' + key,
            title: formula ? `${hint} — ${resolved}, from the formula` : hint,
            value: isHex(resolved) ? resolved : '#000000',
            // A formula decides the colour; dragging the picker would only be overwritten on the
            // next draw, so it shows the answer and does not pretend to take one.
            disabled: formula ? '' : null,
            oninput: (event) => {
              writeProperty(target, key, event.target.value, { kind: 'text' });
              text.value = event.target.value;
            },
          })
          : h('span', {
            class: 'gfd-swatch gfd-swatch-bad',
            'data-testid': 'colour-bad-' + key,
            title: resolved
              ? `${hint} — "${resolved}" is not a colour`
              : `${hint} — no colour set`,
            text: resolved ? '✕' : '',
          }));
      };
      expressionChecks.push(sync);
      sync();

      return [slot, text, h('button', {
        class: 'gfd-clear',
        type: 'button',
        title: 'Clear',
        onclick: () => {
          delete target.bind?.[key];
          setLiteral(target, key, '');
          text.value = '';
          broadcast(target, key);
          renderCanvas();
          markDirty();
        },
      }, '×')];
    }

    // A colour is chosen twice — once for each theme — and the two are decided together, so they
    // sit side by side on one row under a Light and Dark caption. Each half binds on its own,
    // because a fill that follows a value is the everyday reason to want an expression; a bound
    // half takes the whole row, where an expression can actually be read.
    const COLOUR_ROWS = [
      ['Text', 'color.light', 'color.dark', 'Text colour'],
      ['Fill', 'fill.light', 'fill.dark', 'Background'],
    ];

    function colourSlot(target, key, hint) {
      const formula = isFormula(target.bind?.[key]);
      const differs = mixed(target, key);
      return h('div', {
        class: 'gfd-colour-slot' + (formula ? ' bound' : '') + (differs ? ' mixed' : ''),
        'data-property': key,
        title: differs ? `${hint} — the selected controls differ here` : null,
      }, ...colourEditor(target, key, hint));
    }

    // Which of the two columns is the one on screen right now. Choosing a colour for the theme you
    // are not looking at and seeing nothing change is the easiest mistake this panel can invite,
    // so the column in effect says so.
    function activeScheme() {
      if (model.theme === 'light' || model.theme === 'dark') return model.theme;
      return document.documentElement.dataset.theme === 'light' ? 'light' : 'dark';
    }

    function pairCaption(text, scheme) {
      const active = scheme === activeScheme();
      return h('span', {
        class: 'gfd-pair-caption' + (active ? ' active' : ''),
        title: active
          ? `${text} — the theme you are looking at, so this column is what you see`
          : `${text} — not the theme you are looking at; switch the component's theme above to see it`,
        text,
      });
    }

    const colourRows = (target) => [
      h('div', { class: 'gfd-row wide head' },
        h('span', { class: 'gfd-row-label' }),
        h('div', { class: 'gfd-cell pair' },
          pairCaption('Light', 'light'),
          pairCaption('Dark', 'dark'))),
      ...COLOUR_ROWS.map(([label, light, dark, hint]) => h('div', { class: 'gfd-row wide' },
        h('span', { class: 'gfd-row-label', title: hint, text: label }),
        h('div', { class: 'gfd-cell pair' },
          colourSlot(target, light, `${hint} on a light theme`),
          colourSlot(target, dark, `${hint} on a dark theme`)))),
    ];

    // Aligning against the component is the everyday designer action that coordinates make tedious.
    // Each button sets one axis and leaves the other alone. With several controls selected it is
    // the box around them that gets aligned and they all travel together, so the arrangement
    // someone built survives being put somewhere else.
    function alignmentRow(control) {
      const align = (axis, where) => {
        const box = selectionBox();
        const size = axis === 'x' ? box.w : box.h;
        const extent = axis === 'x' ? model.doc.width : model.doc.height;
        const to = where === 'start' ? 0
          : where === 'center' ? Math.max(0, Math.round((extent - size) / 2))
            : Math.max(0, extent - size);
        const delta = to - (axis === 'x' ? box.x : box.y);
        moveSelection(axis === 'x' ? delta : 0, axis === 'x' ? 0 : delta);
      };

      // A group can be aligned as long as some part of it can move; one control cannot if its own
      // coordinate is decided by an expression.
      const stuck = (axis) => selectedControls().every((target) => isBound(target, axis));

      const button = (label, title, axis, where) => h('button', {
        class: 'gfd-align-btn',
        type: 'button',
        title,
        disabled: stuck(axis) ? '' : null,
        'data-testid': `align-${axis}-${where}`,
        onclick: () => align(axis, where),
      }, label);

      return row(control, null, 'Align', () => h('span', { class: 'gfd-align' },
        button('⇤', 'Align left', 'x', 'start'),
        button('⇔', 'Centre horizontally', 'x', 'center'),
        button('⇥', 'Align right', 'x', 'end'),
        button('⤒', 'Align top', 'y', 'start'),
        button('⇕', 'Centre vertically', 'y', 'center'),
        button('⤓', 'Align bottom', 'y', 'end')));
    }

    // The box around everything selected, edited as one. Left and Top move the group; Width and
    // Height scale it, keeping the parts in proportion. Each is a plain row with no ƒ, because an
    // expression belongs to a property of a control and this box is not one.
    function groupGeometryRows() {
      const box = selectionBox();
      // On change rather than on every keystroke: half-typed numbers are still numbers, and
      // scaling a group to 1px on the way to 120 would flatten it before you finished typing.
      const field = (label, value, hint, apply) => row(null, null, label, () => h('input', {
        type: 'number',
        class: 'gfd-number',
        value,
        'data-testid': 'group-' + label.toLowerCase(),
        onchange: (event) => apply(Number(event.target.value) || 0),
      }), { hint });

      return [
        field('Left', box.x, 'The left edge of everything selected — moves them together',
          (next) => moveSelection(next - box.x, 0)),
        field('Top', box.y, 'The top edge of everything selected — moves them together',
          (next) => moveSelection(0, next - box.y)),
        field('Width', box.w, 'The width of the whole selection — scales it',
          (next) => box.w > 0 && scaleSelection(next / box.w, 1)),
        field('Height', box.h, 'The height of the whole selection — scales it',
          (next) => box.h > 0 && scaleSelection(1, next / box.h)),
      ];
    }

    // ---- the component's own pages ----

    function componentEditors(which) {
      if (which === 'appearance') {
        return [
          heading('Size'),
          row(model.doc, 'width', 'Width', numberEditor(model.doc, 'width')),
          row(model.doc, 'height', 'Height', numberEditor(model.doc, 'height')),
          row(model.doc, null, 'Scroll', () => h('input', {
            type: 'checkbox',
            class: 'gfd-check',
            checked: model.doc.showScrollbars ? '' : null,
            onchange: (event) => {
              model.doc.showScrollbars = event.target.checked;
              renderCanvas();
              markDirty();
            },
          }), { hint: 'Show scrollbars when a control sits beyond the component\'s edge' }),
          row(model.doc, null, 'Resize', () => h('input', {
            type: 'checkbox',
            class: 'gfd-check',
            checked: model.doc.resizable ? '' : null,
            onchange: (event) => {
              model.doc.resizable = event.target.checked;
              renderCanvas();
              markDirty();
            },
          }), { hint: 'Let the viewer resize the component' }),
          row(model.doc, null, 'Isolate', () => h('input', {
            type: 'checkbox',
            class: 'gfd-check',
            checked: model.doc.isolated ? '' : null,
            onchange: (event) => {
              model.doc.isolated = event.target.checked;
              renderCanvas();
              renderProperties();
              markDirty();
            },
          }), { hint: 'Ignore workspace styles and start from the browser\'s own' }),
          heading('Colour'),
          ...colourRows(model.doc),
          cssSection('component-custom', model.doc, `${componentSelector()} {\n  \n}`),
          // The component's own rule, not the whole sheet: a control's rules belong to that control.
          // The reset an isolated component adds is the component's too, and it is shown in the layer it is
          // really in — a rule that loses to everything you write reads very differently from one
          // that does not.
          section('component-generated', 'Generated CSS',
            generatedBlock('generated-sheet', () => {
              const reset = isolationResetCss();
              return [
                generatedCssForComponent(),
                ...(reset ? [`@layer gridlet {\n${reset}\n}`] : []),
              ].join('\n\n');
            })),
        ];
      }

      return componentSettings();
    }

    // What the component is, what it shows, and what it does, in that order. The source is the middle
    // of those three: a component is named, then pointed at rows, then given behaviour.
    function componentSettings() {
      return [
        ...componentSettingsRows(),
        ...dataSourceEditors(),
        heading('Behaviour'),
        ...moduleRows(),
      ];
    }

    // Which modules this component runs. The component names them and nothing more: what they do is in the
    // module, edited in its own tab, so the document stays a description of the component.
    function moduleRows() {
      // Gridlet's own modules are libraries to import, not behaviour to attach: they have no component
      // to run against. They are offered in the code tabs, not as something to tick here.
      // Ticking anything here is also what puts a file's exports in reach of this component's
      // expressions, so the scope follows the tick either way.
      const attachmentChanged = () => {
        markDirty();
        renderProperties();
        if (model.mode === 'preview') restartBehaviour();
        else refreshExpressionScope();
      };

      // One class of one file. Offered under the file, and only once the component already names that
      // file: what a module exports is not knowable without reading it, and reading every module
      // in the workspace to draw a list would run all of them.
      const classRow = (script, className, ticked) => h('div',
        { class: 'gfd-row gfd-module-class', 'data-module': script.name, 'data-class': className },
        h('label', {
          class: 'gfd-row-label gfd-module-label',
          title: `Run ${className} from ${script.name} in this component`,
        },
          h('input', {
            type: 'checkbox',
            class: 'gfd-check',
            checked: ticked ? '' : null,
            'data-testid': `module-class-${script.name}-${className}`,
            onchange: (event) => {
              model.doc.modules = event.target.checked
                ? [...model.doc.modules, { module: script.name, class: className }]
                : model.doc.modules.filter((entry) => moduleFileOf(entry) !== script.name
                  || moduleClassOf(entry) !== className);
              attachmentChanged();
            },
          }),
          h('span', { class: 'gfd-module-name', text: className })),
        h('span', { class: 'gfd-bind-gap' }));

      const rows = model.scripts.filter((script) => !script.readOnly).flatMap((script) => {
        const entries = model.doc.modules.filter((entry) => moduleFileOf(entry) === script.name);
        // The file's own tick is the older meaning kept: run whatever it exports as its default.
        const attached = entries.some((entry) => moduleClassOf(entry) === null);
        const row = h('div', { class: 'gfd-row', 'data-module': script.name },
          h('label', { class: 'gfd-row-label gfd-module-label', title: `Run ${script.name} in this component` },
            h('input', {
              type: 'checkbox',
              class: 'gfd-check',
              checked: attached ? '' : null,
              'data-testid': 'module-' + script.name,
              onchange: (event) => {
                model.doc.modules = event.target.checked
                  ? [...model.doc.modules, script.name]
                  : model.doc.modules.filter((entry) => moduleClassOf(entry) !== null
                    || moduleFileOf(entry) !== script.name);
                attachmentChanged();
              },
            }),
            h('span', { class: 'gfd-module-name', text: script.name })),
          h('div', { class: 'gfd-cell' },
            h('button', {
              class: 'gfd-module-open',
              type: 'button',
              title: `Edit ${script.name}`,
              onclick: () => openCodeTab(script.name),
            }, 'Edit')),
          h('span', { class: 'gfd-bind-gap' }));

        const classes = behaviour.classes.get(script.name) || [];
        return [row, ...classes.map((className) => classRow(script, className,
          entries.some((entry) => moduleClassOf(entry) === className)))];
      });

      if (!rows.length) {
        rows.push(note('No modules yet. A module is ordinary JavaScript that runs when the component does.'));
      }

      rows.push(h('button', {
        class: 'gfd-module-new',
        type: 'button',
        'data-testid': 'component-module-new',
        onclick: () => newModule(async (script) => {
          // The module is new to the whole workspace, not just to this component, so the list in the
          // sidebar learns about it at the same moment this one does.
          await Promise.all([loadScripts(), codeSidebar.refresh()]);
          // A module made from a component is one that component meant to run.
          model.doc.modules = [...model.doc.modules, script.name];
          markDirty();
          renderProperties();
          openCodeTab(script.name);
        }),
      }, 'New module'));

      // What the last run threw, and any export that could not keep its own name, beside the
      // modules they came from.
      const problems = [...behaviour.errors, ...behaviour.clashes];
      if (problems.length) {
        rows.push(h('div', { class: 'gfd-code-problems' }, problems.map((problem) =>
          h('div', { class: 'gfd-code-problem' },
            h('strong', { text: problem.name }),
            h('span', { text: ' — ' + problem.message })))));
      }

      return rows;
    }

    function componentSettingsRows() {
      const layoutSelect = h('select', {
        onchange: (event) => {
          model.doc.layout = event.target.value;
          renderCanvas();
          markDirty();
        },
      }, Object.entries(LAYOUTS).map(([value, label]) =>
        h('option', { value, text: label, selected: model.doc.layout === value ? '' : null })));

      return [
        heading('Component'),
        row(model.doc, null, 'Name', () => {
          const input = h('input', {
            type: 'text',
            oninput: (event) => {
              model.name = event.target.value;
              tab.title = model.name || 'New component';
              refreshTabs();
              subjectName.textContent = model.name;
              // The component's selector is built from its name, so the sheet is rebuilt when it
              // changes.
              renderCanvas();
              markDirty();
            },
          });
          input.value = model.name;
          return input;
        }),
        row(model.doc, null, 'Layout', () => layoutSelect),
        ...identityRows(model.doc),
        ...eventRows(model.doc, COMPONENT_EVENTS),
      ];
    }

    // The HTML id and classes the component or control carries, so an operator's own CSS can address it
    // the way they would address any other element, and the tooltip it shows. All three are plain
    // HTML attributes, and all three take an expression like everything else — a tip that names the
    // value it is explaining is the reason to want one.
    //
    // On a control they land on the element itself — the button, the input — rather than on the box
    // the designer positions it with, so a rule written against them styles what it looks like it
    // is styling. So does the control's name: `[data-name="button2"]` is the button. The box the
    // designer positions is `[data-control-box="button2"]`.
    const identityRows = (target, group = false) => [
      // An id has to be unique in the page, so a group does not get to set one.
      ...(group ? [] : [row(target, 'elementId', 'Id', textEditor(target, 'elementId'),
        { hint: 'The HTML id of the control itself — what a <label for> points at' })]),
      row(target, 'classes', 'Class', textEditor(target, 'classes'),
        { hint: 'HTML classes on the control itself, space separated' }),
      row(target, 'tip', 'Tip', textEditor(target, 'tip'),
        { hint: 'Tooltip shown when the pointer rests here — the HTML title attribute' }),
    ];

    // A component reads through a published endpoint, never through SQL of its own. That keeps the
    // endpoint's authorization and typed parameters as the only way in, and means a component cannot
    // reach data that was not deliberately published.
    function dataSourceEditors() {
      const chosen = model.endpoints.find((e) => e.id === model.doc.source?.endpointId) || null;

      const sourceSelect = h('select', {
        'data-testid': 'component-source',
        onchange: (event) => {
          const endpoint = model.endpoints.find((e) => e.id === event.target.value) || null;
          model.doc.source = endpoint
            ? { endpointId: endpoint.id, route: endpoint.route, parameters: {} }
            : null;
          markDirty();
          loadRows(true).then(() => { renderProperties(); renderCanvas(); });
        },
      },
        h('option', { value: '', text: 'None', selected: chosen ? null : '' }),
        model.endpoints.map((endpoint) => h('option', {
          value: endpoint.id,
          text: `${endpoint.name} (${endpoint.route})`,
          selected: chosen?.id === endpoint.id ? '' : null,
        })));

      const editors = [heading('Source'), row(model.doc, null, 'Endpoint', () => sourceSelect,
        { hint: 'A published GET endpoint' })];

      if (!model.endpoints.length) {
        editors.push(note('No GET endpoints are published yet. Publish a query as a GET endpoint to bind a component to it.'));
      }

      // Endpoint parameters are declared by the endpoint, so the component fills in values rather than
      // inventing arguments the endpoint never accepted.
      const parameters = chosen?.parameters || [];
      if (parameters.length) editors.push(heading('Parameters'));
      for (const parameter of parameters) {
        editors.push(row(model.doc, null, parameter.name, () => {
          const input = h('input', {
            type: 'text',
            placeholder: parameter.required ? 'required' : 'optional',
            oninput: (event) => {
              model.doc.source.parameters = {
                ...model.doc.source.parameters,
                [parameter.name]: event.target.value,
              };
              markDirty();
            },
          });
          input.value = model.doc.source.parameters?.[parameter.name] ?? '';
          return input;
        }, { hint: parameter.required ? `${parameter.name} (required)` : parameter.name }));
      }

      if (chosen) {
        editors.push(heading('Columns'));
        editors.push(model.columns.length
          ? h('div', { class: 'gfd-chips' }, model.columns.map((column) =>
            h('span', { class: 'gfd-chip static', text: column })))
          : note('Read the source to discover its columns — open Preview, or fill in the parameters above.'));
      }

      return editors;
    }

    // ---- a control's pages ----

    function controlEditors(control, which) {
      const spec = CATALOGUE[control.type];
      const selected = selectedControls();
      const group = selected.length > 1;

      if (which === 'appearance') {
        return [
          heading(group ? 'Selection' : 'Layout'),
          ...(group ? groupGeometryRows() : [
            row(control, 'x', 'Left', numberEditor(control, 'x')),
            row(control, 'y', 'Top', numberEditor(control, 'y')),
            row(control, 'w', 'Width', numberEditor(control, 'w')),
            row(control, 'h', 'Height', numberEditor(control, 'h')),
          ]),
          alignmentRow(control),
          heading('Colour'),
          ...colourRows(control),
          // Custom CSS and the cascade behind it are written against one control's own selector,
          // so they belong to one control. The rest of the page still works on all of them.
          ...(group
            ? [note('Colours apply to everything selected. Select one control on its own for its CSS and where its styling comes from.')]
            : cascadeSections(control)),
        ];
      }

      const EDITORS = {
        boolean: checkEditor, lines: linesEditor, number: numberEditor, text: textEditor,
      };

      // Only the properties every selected control actually has: offering Placeholder for a
      // selection that includes a label would be offering to set something that goes nowhere.
      // The property a control shows its value in has its own row in the Value group above, so it
      // is not offered twice on the page it now shares with it.
      const valueKey = sharedValueKey(spec, group);
      const shared = (group
        ? spec.properties.filter((property) =>
          selected.every((other) => hasProperty(other, property.key)))
        : spec.properties).filter((property) => property.key !== valueKey);

      const editors = shared.map((property) => {
        const editor = EDITORS[property.kind] || textEditor;
        return row(control, property.key, property.label,
          editor(control, property.key, { after: property.after }),
          { block: property.kind === 'lines' });
      });

      return [
        heading(group ? `${selected.length} controls` : spec.title),
        // A name and an HTML id have to be unique, so they are the two things a group cannot set
        // at once. Everything else on this page applies to all of them.
        ...(group ? [] : [
          row(control, null, 'Name', () => {
            const input = h('input', {
              type: 'text',
              'data-testid': 'control-name',
              oninput: (event) => {
                control.name = event.target.value;
                subjectName.textContent = control.name;
                renderCanvas();
                markDirty();
              },
            });
            input.value = control.name;
            return input;
          }, { hint: 'What expressions and CSS call this control' }),
        ]),
        // What it shows, next to what it is called. A control that displays nothing says so only
        // when its kind has something worth saying about why.
        ...controlValueEditors(control, spec, group),
        // The columns a value can be bound to are a group of their own, so what follows them needs
        // a heading of its own or it reads as more of them.
        heading('Element'),
        ...identityRows(control, group),
        ...(editors.length ? [heading('Properties'), ...editors] : []),
        ...(group && editors.length < spec.properties.length
          ? [note('Only the properties every selected control has are shown.')]
          : []),
        // A handler belongs to one control, the way a name does: two controls sharing a click is
        // two handlers that happen to call the same function.
        ...(group ? [] : eventRows(control, CONTROL_EVENTS)),
        ...elsewhereFormulaRows(control, spec, group),
        h('button', {
          class: 'danger gfd-delete',
          onclick: () => deleteSelection(),
        }, group ? `Delete ${selected.length} controls` : 'Delete control'),
      ];
    }

    // Which of the component's own rules land on this control. A control styled by a class or an id
    // shows nothing in its own boxes, and the styling looks like it came from nowhere; this says
    // where it came from. Matching is done by asking the element itself, so it agrees with the
    // browser rather than with a guess about selectors.
    function matchingComponentRules(control) {
      // A control is a box wrapping the element you see, and a component rule may target either. Both
      // are tested, or a rule written against the inner element would look like it matched nothing.
      const box = canvas.querySelector(`[data-id="${control.id}"]`);
      const element = {
        matches: (selector) => Boolean(box?.matches(selector))
          || Boolean(box?.firstElementChild?.matches(selector)),
      };
      if (!box || !model.doc.css?.trim()) return [];

      const matches = [];
      for (const [, selectors, body] of model.doc.css.matchAll(/([^{}]+)\{([^{}]*)\}/g)) {
        const applicable = selectors.split(',')
          .map((selector) => selector.trim())
          .filter((selector) => {
            try { return selector && element.matches(selector); } catch { return false; }
          });
        if (applicable.length) {
          matches.push({ selector: applicable.join(', '), body: body.trim() });
        }
      }
      return matches;
    }

    // The four layers that decide how a control looks, strongest first, which is the order dev
    // tools uses. Each layer claims the properties it sets; a weaker layer shows its own version
    // of a claimed property struck through.
    function cascadeSections(control) {
      const inheritedBody = h('div');
      const generatedBody = h('div');
      const browserBody = h('div');

      // The three read-only layers are recomputed whenever the sheet changes. The Custom CSS box
      // is deliberately left alone: it may have the cursor in it.
      const refresh = () => {
        const parts = cascadeLayers(control);
        inheritedBody.replaceChildren(...parts.inherited);
        generatedBody.replaceChildren(...parts.generated);
        browserBody.replaceChildren(...parts.browser);
        inheritedSummary.textContent = `Inherited from the component (${parts.inheritedCount})`;
      };

      const custom = cssSection('control-custom', control,
        `${innerSelectorFor(control)} {\n  \n}`);
      const inheritedSection = section('control-inherited', 'Inherited from the component', inheritedBody);
      const inheritedSummary = inheritedSection.querySelector('summary');

      cascadeRefresh = refresh;
      refresh();

      return [
        custom,
        inheritedSection,
        section('control-generated', 'Generated CSS', generatedBody,
          note('A control is a box that places it, and the element inside that you see. '
            + 'The first rule positions the box from the panel\'s measurements; the second fills '
            + 'the box with the element and gives it the colours. Both are written as a variable '
            + 'and then the property that reads it, so your own CSS can change either.')),
        section('control-browser', 'From the browser', browserBody),
      ];
    }

    function cascadeLayers(control) {
      const claimed = new Set();

      claim(parseRules(control.css), claimed);

      const inherited = matchingComponentRules(control)
        .map((match) => ({ selector: match.selector, declarations: parseRules(`x {${match.body}}`)[0]?.declarations || [] }));
      const inheritedRendered = renderRules(inherited, new Set(claimed), 'inherited-rule');
      claim(inherited, claimed);

      // Structure and geometry first, then the kind's default appearance, which is weaker than both
      // and weaker than anything written above it. Listing them together keeps this the one place
      // that answers "where did that come from?", in the order the browser answers it.
      const generated = [
        ...parseRules(generatedCssFor(control)),
        ...parseRules(defaultCssFor(control)),
      ];
      const generatedRendered = renderRules(generated, new Set(claimed), 'generated-css');
      claim(generated, claimed);

      const element = canvas.querySelector(`[data-id="${control.id}"] > :first-child`);
      const defaults = element ? browserDefaultsFor(element) : [];
      const browser = defaults.length
        ? [{ selector: element.tagName.toLowerCase(), declarations: defaults }]
        : [];

      return {
        inheritedCount: inherited.length,
        inherited: inherited.length ? inheritedRendered
          : [h('p', { class: 'field-note gfd-note' }, 'No rule in the component\'s CSS matches this control.')],
        generated: generatedRendered,
        browser: browser.length
          ? [
            ...renderRules(browser, claimed, 'browser-defaults'),
            h('p', { class: 'field-note gfd-note' },
              'Read from an empty document, so this is the browser\'s own styling before any CSS.'),
          ]
          : [h('p', { class: 'field-note gfd-note' }, 'The browser applies nothing of its own to this element.')],
      };
    }

    // The value slot and everything that can feed it. The value is an expression
    // by nature — there is no literal behind it — so the box is always the expression box, and the
    // columns are chips because clicking one is faster than typing it and cannot misspell it.
    // The property the whole selection displays its value in, or nothing. Where a value lives
    // differs by kind — a label's is its text, a text box's is its own slot — so a mixed selection
    // has no single property to write, and says so instead of writing to whichever kind happened
    // to be picked first.
    function sharedValueKey(spec, group) {
      if (!spec.bindable) return null;
      const valueKey = valueKeyOf(spec);
      if (group && !selectedControls().every((other) =>
        CATALOGUE[other.type].bindable && valueKeyOf(CATALOGUE[other.type]) === valueKey)) return null;
      return valueKey;
    }

    function controlValueEditors(control, spec, group = false) {
      // A kind that displays nothing has nothing to bind. Most say so by having no Value group at
      // all; one with something worth explaining — a pager follows the component's rows rather than a
      // value of its own — says that instead.
      if (!spec.bindable) return spec.dataNote ? [note(spec.dataNote)] : [];

      const valueKey = sharedValueKey(spec, group);
      if (!valueKey) {
        return [note('The selected controls hold their value in different places. Select controls of one kind, or one control on its own, to bind a value.')];
      }

      // For a label the value is its Text: one property, and now one row for it, under the name
      // its own kind gives it. A kind whose value has no property of its own — a text box's — is
      // shown as Value.
      const own = spec.properties.find((property) => property.key === valueKey);
      const editors = [
        heading('Value'),
        row(control, valueKey, own?.label || 'Value',
          () => propertyBox(control, valueKey, { placeholder: '=data.Column' }),
          { hint: 'What this control displays' }),
      ];

      if (!model.doc.source) {
        editors.push(note('Bind to another control now, or choose a source on the component\'s own Settings page to bind to a column.'));
      } else if (model.columns.length) {
        const pick = (expression) => {
          control.bind[valueKey] = FORMULA + expression;
          broadcast(control, valueKey);
          renderCanvas();
          renderProperties();
          markDirty();
        };

        editors.push(heading('Columns'));
        editors.push(h('div', { class: 'gfd-chips' },
          // The row itself, beside its columns: an object shown as text is JSON, which is the
          // quickest way to see what a source actually returns. Give a text box Multiline and it
          // has the room for it.
          h('button', {
            class: 'gfd-chip row',
            type: 'button',
            'data-testid': 'column-whole-row',
            title: 'Show the whole row here, as JSON',
            onclick: () => pick('data'),
          }, '(row)'),
          model.columns.map((column) => h('button', {
            class: 'gfd-chip',
            type: 'button',
            'data-testid': 'column-' + column,
            title: `Show ${column} here`,
            onclick: () => pick(dataReference(column)),
          }, column))));
      } else {
        editors.push(note('Read the source to discover its columns — open Preview, or fill in its parameters on the component\'s own Settings page.'));
      }

      return editors;
    }

    // Formulas set somewhere other than here — a width that follows another control, a colour that
    // follows a value — gathered so this page answers "what on this control follows something
    // else?" without sending anyone hunting through the other one. A property with its own row
    // above is not repeated: it is already showing its formula.
    function elsewhereFormulaRows(control, spec, group) {
      if (group) return [];
      const shown = new Set([
        ...(spec.bindable ? [valueKeyOf(spec)] : []),
        ...spec.properties.map((property) => property.key),
        'classes', 'elementId', 'tip',
      ]);
      const bound = bindableKeys(control)
        .filter((key) => !shown.has(key) && isFormula(control.bind[key]));
      if (!bound.length) return [];

      return [
        heading('Also from a formula'),
        ...bound.map((key) => row(control, key,
          BINDING_LABELS[key] || key,
          () => propertyBox(control, key))),
      ];
    }

    // What a bound property is called where it is out of the context that named it on its own
    // page.
    const BINDING_LABELS = {
      x: 'Left', y: 'Top', w: 'Width', h: 'Height',
      'color.light': 'Text L', 'color.dark': 'Text D',
      'fill.light': 'Fill L', 'fill.dark': 'Fill D',
    };

    // Addressed by name rather than by id: names are unique within a component, and an attribute
    // selector cannot collide with anything in the surrounding page. A name is whatever someone
    // typed, so the quotes and backslashes that would end the selector early are escaped.
    const quoted = (value) => value.replace(/["\\]/g, '\\$&');

    // The positioned box. It is the designer's own, so it carries the name under an attribute of
    // the designer's own — leaving the plain `[data-name]` to mean the control itself.
    const selectorFor = (control) => control.name
      ? `[data-control-box="${quoted(control.name)}"]`
      : `[data-id="${control.id}"]`;

    // The element you actually see. Geometry belongs to the box; appearance belongs to this. Both
    // are emitted, so the panel shows every rule the designer applies and nothing arrives from a
    // stylesheet you cannot read.
    const innerSelectorFor = (control) => control.name
      ? `[data-name="${quoted(control.name)}"]`
      : `[data-id="${control.id}"] > :first-child`;

    function generatedCssFor(control) {
      const view = pass.viewOf(control);

      // An isolated component deliberately has no default appearance: reinstating it here would undo
      // the reset the component just asked for. Colours chosen in the panel still apply — they are the
      // operator's own decision, not Gridlet's styling. The fallback for a theme left empty is a
      // colour, never a keyword: see themedColor.
      const style = model.doc.isolated ? {} : styleOf(CATALOGUE[control.type], view);
      const text = themedColor(view.colors, 'text', style.color || 'currentColor');
      const background = themedColor(view.colors, 'background', style.background || 'transparent');

      // Every value the panel sets becomes a variable and then the property that reads it, in the
      // same rule. Nothing is left standing on a stylesheet you cannot see: what positions and
      // colours this control is all here, and custom CSS can redefine either the variable or the
      // property that uses it.
      const rules = [[
        `${selectorFor(control)} {`,
        `  --gfd-left: ${view.x}px;`,
        `  --gfd-top: ${view.y}px;`,
        `  --gfd-width: ${view.w}px;`,
        `  --gfd-height: ${view.h}px;`,
        ...(text ? [`  --gfd-color: ${text};`] : []),
        ...(background ? [`  --gfd-fill: ${background};`] : []),
        '  position: absolute;',
        '  left: var(--gfd-left);',
        '  top: var(--gfd-top);',
        '  width: var(--gfd-width);',
        '  height: var(--gfd-height);',
        '}',
      ].join('\n')];

      // The element inside the box fills it, whatever it looks like. This is structure rather than
      // appearance, so it is not one of the defaults below that a class is meant to beat.
      rules.push([
        `${innerSelectorFor(control)} {`,
        '  box-sizing: border-box;',
        '  width: 100%;',
        '  height: 100%;',
        '}',
      ].join('\n'));

      return rules.join('\n\n');
    }

    // How a control looks before anybody says otherwise: what its kind comes with, and the colours
    // the panel set, read from the variables defined on the box.
    //
    // These go in a cascade layer, and that is the whole point of them being separate. An unlayered
    // rule beats a layered one however weak its selector is, so `.btn { border: 1px solid red }`
    // does what it says — the class is on the button, and it no longer has to out-specify a
    // selector Gridlet wrote. Structure and geometry stay out of the layer: a default border is
    // Gridlet's opinion, but a control filling its own box is not.
    function defaultCssFor(control) {
      const view = pass.viewOf(control);
      const style = model.doc.isolated ? {} : styleOf(CATALOGUE[control.type], view);
      const text = themedColor(view.colors, 'text', style.color || 'currentColor');
      const background = themedColor(view.colors, 'background', style.background || 'transparent');

      const declarations = {
        ...style,
        ...(text ? { color: 'var(--gfd-color)' } : {}),
        ...(background ? { 'background-color': 'var(--gfd-fill)' } : {}),
      };
      if (!Object.keys(declarations).length) return '';

      return [
        `${innerSelectorFor(control)} {`,
        ...Object.entries(declarations).map(([property, value]) => `  ${property}: ${value};`),
        '}',
      ].join('\n');
    }

    // The component is addressed by its own name, the same way its controls are, rather than by the
    // designer's internal class. A rule you can read is a rule you can copy and change.
    const componentSelector = () => `[data-component="${model.name || 'component'}"]`;

    function generatedCssForComponent() {
      const component = pass.componentView();
      const surfaceDefault = model.doc.isolated ? 'Canvas' : 'var(--panel)';
      const textDefault = model.doc.isolated ? 'CanvasText' : 'var(--text)';
      // An isolated component starts from the browser's own colours, so those are what a colour left
      // empty falls back to. They belong in this rule, beside the colours that were chosen: a
      // second rule with the same selector further down the sheet would beat both of them.
      const text = themedColor(component.colors, 'text', textDefault)
        ?? (model.doc.isolated ? 'CanvasText' : null);
      const background = themedColor(component.colors, 'background', surfaceDefault)
        ?? (model.doc.isolated ? 'Canvas' : null);

      const rules = [[
        `${componentSelector()} {`,
        `  --gfd-component-width: ${component.width}px;`,
        `  --gfd-component-height: ${component.height}px;`,
        ...(text ? [`  color: ${text};`] : []),
        ...(background ? [`  background-color: ${background};`] : []),
        '}',
      ].join('\n')];

      return rules.join('\n\n');
    }

    // What an isolated component starts from: the browser's own styling, with Gridlet's taken back off.
    //
    // `revert` drops every author-level rule — Gridlet's and the designer's — and it is aimed at
    // the element inside each control and its contents, never at the control box, so positioning
    // and the designer's selection chrome survive. The system colours track the component's colour
    // scheme, so the theme switch still works.
    //
    // It goes in the same layer as the kind defaults, and for the same reason. `all: revert` on a
    // selector Gridlet wrote would otherwise outrank a plain `.btn { border: 1px solid red }` and
    // undo it — asking for a clean slate would quietly mean losing the CSS you wrote on top of it.
    function isolationResetCss() {
      if (!model.doc.isolated) return '';
      return [
        '.gfd-control > :first-child,',
        '.gfd-control > :first-child * {',
        '  all: revert;',
        '  box-sizing: border-box;',
        '}',
        '',
        '.gfd-control > :first-child {',
        '  width: 100%;',
        '  height: 100%;',
        '}',
      ].join('\n');
    }

    // The whole sheet the designer is applying, in document order.
    function generatedCssSource() {
      const rules = [generatedCssForComponent()];
      walk(model.doc.controls, (control) => rules.push(generatedCssFor(control)));
      return rules.join('\n\n');
    }

    // What each control looks like before anybody says otherwise, kept apart so it can be put in a
    // layer. Everything in here loses to a rule you write, however plain that rule's selector is.
    function defaultCssSource() {
      const rules = [isolationResetCss()].filter(Boolean);
      walk(model.doc.controls, (control) => {
        const css = defaultCssFor(control);
        if (css) rules.push(css);
      });
      return rules.join('\n\n');
    }

    // Custom styling in one sheet, loaded after the generated rules so it wins: each control's own
    // declarations first, then the component's rules, which can therefore override a control's.
    // The component's rules first, then each control's own, so a control's CSS overrides the component's at
    // equal specificity. The most local place you can put a rule is the one that wins.
    function customCssSource() {
      const rules = [];
      if (model.doc.css?.trim()) rules.push(model.doc.css.trim());
      // Both boxes take the same thing: CSS rules, scoped to this component. A control's box is where
      // its rules live, not a different language.
      walk(model.doc.controls, (control) => {
        const css = control.css?.trim();
        if (css) rules.push(css);
      });
      return rules.join('\n\n');
    }

    // The panel's generated-CSS block is a view of the sheet, so it is refreshed wherever the
    // sheet is. Editing a colour or a coordinate would otherwise leave visibly stale text next to
    // a canvas that had already changed.
    let generatedView = null;
    let cascadeRefresh = null;
    // Whether each expression in the panel still works out. Refreshed rather than rebuilt, because
    // the box being marked is usually the one with the cursor in it.
    let expressionChecks = [];

    function applyStyles() {
      pass = resolveAll();
      // Scoped first and wrapped afterwards: the scoper reads selectors, and an @layer block is not
      // one. What comes out is a layer holding rules that are still confined to this component.
      const defaults = scopeCss(defaultCssSource(), surface);
      defaultStyle.textContent = defaults ? `@layer gridlet {\n${defaults}\n}` : '';
      generatedStyle.textContent = scopeCss(generatedCssSource(), surface);
      customStyle.textContent = scopeCss(customCssSource(), surface);
      if (generatedView?.element.isConnected) {
        generatedView.element.textContent = generatedView.read();
      }
      for (const check of expressionChecks) check();
      cascadeRefresh?.();
    }

    // ---- cascade view ----
    // Rules are shown as elements rather than text so a declaration a stronger layer also sets can
    // be struck through, the way dev tools does it. The comparison is by property name: it does
    // not reason about shorthands, and it treats each layer as a whole rather than weighing
    // selectors inside the custom CSS.

    function parseRules(css) {
      const rules = [];
      for (const [, selector, body] of (css || '').matchAll(/([^{}]+)\{([^{}]*)\}/g)) {
        const declarations = body.split(';')
          .map((declaration) => declaration.trim())
          .filter(Boolean)
          .map((declaration) => {
            const at = declaration.indexOf(':');
            return at < 0 ? null : [declaration.slice(0, at).trim(), declaration.slice(at + 1).trim()];
          })
          .filter(Boolean);
        rules.push({ selector: selector.trim(), declarations });
      }
      return rules;
    }

    function renderRules(rules, claimed, testId) {
      return rules.map((rule) => h('pre', { class: 'gfd-generated-css', 'data-testid': testId },
        rule.selector, ' {\n',
        ...rule.declarations.map(([property, value]) => h('span', {
          class: 'gfd-decl' + (claimed.has(property) ? ' overridden' : ''),
          title: claimed.has(property) ? 'Overridden by a stronger rule above' : null,
        }, `  ${property}: ${value};`)),
        '}'));
    }

    // Claims every property a layer sets, so weaker layers below can show theirs as overridden.
    function claim(rules, claimed) {
      for (const rule of rules) {
        for (const [property] of rule.declarations) claimed.add(property);
      }
      return claimed;
    }

    function generatedBlock(testId, read) {
      const element = h('pre', { class: 'gfd-generated-css', 'data-testid': testId, text: read() });
      generatedView = { element, read };
      return element;
    }

    function deleteSelection() {
      const controls = selectedControls();
      if (!controls.length) return;
      closeCssTabs(controls.map((control) => control.id));
      for (const control of controls) {
        const list = findParentList(model.doc, control.id);
        list?.splice(list.findIndex((c) => c.id === control.id), 1);
      }
      model.selection = [];
      renderCanvas();
      renderProperties();
      markDirty();
    }

    // ---- the selection as one thing ----
    // Several controls picked together behave like the block they look like: the box around them
    // is what the Appearance page shows, moving works on all of them at once, and their
    // arrangement survives both.

    function selectionBox() {
      const views = selectedControls().map((control) => pass.viewOf(control));
      if (!views.length) return null;
      const left = Math.min(...views.map((view) => view.x));
      const top = Math.min(...views.map((view) => view.y));
      return {
        x: left,
        y: top,
        w: Math.max(...views.map((view) => view.x + view.w)) - left,
        h: Math.max(...views.map((view) => view.y + view.h)) - top,
      };
    }

    // A control inside a selected container is carried by it, because its coordinates are relative
    // to that container. Moving it on its own account as well would move it twice as far, so the
    // ones travelling as passengers are dropped from the list before anything shifts.
    function movable(controls = selectedControls()) {
      const chosen = new Set(controls.map((control) => control.id));
      const passengers = new Set();
      walk(model.doc.controls, (control, parent) => {
        if (parent && (chosen.has(parent.id) || passengers.has(parent.id))) {
          passengers.add(control.id);
        }
      });
      return controls.filter((control) => !passengers.has(control.id));
    }

    function moveSelection(dx, dy) {
      if (!dx && !dy) return;
      for (const control of movable()) {
        if (!isBound(control, 'x')) control.x = Math.max(0, control.x + dx);
        if (!isBound(control, 'y')) control.y = Math.max(0, control.y + dy);
      }
      renderCanvas();
      renderProperties();
      markDirty();
    }

    // Resizing a group scales it about its own top-left, so the parts keep their proportions and
    // their spacing rather than all becoming the same size.
    function scaleSelection(factorX, factorY) {
      const box = selectionBox();
      if (!box) return;

      // What is inside a container is measured from that container's own corner, so it scales
      // about zero rather than about the selection's box. Without this a resized panel would keep
      // its frame and leave its contents the size they were.
      const scaleInside = (controls) => {
        for (const child of controls) {
          const view = pass.viewOf(child);
          if (factorX !== 1) {
            if (!isBound(child, 'x')) child.x = Math.max(0, Math.round(view.x * factorX));
            if (!isBound(child, 'w')) child.w = Math.max(GRID, Math.round(view.w * factorX));
          }
          if (factorY !== 1) {
            if (!isBound(child, 'y')) child.y = Math.max(0, Math.round(view.y * factorY));
            if (!isBound(child, 'h')) child.h = Math.max(GRID, Math.round(view.h * factorY));
          }
          if (child.controls) scaleInside(child.controls);
        }
      };

      for (const control of movable()) {
        const view = pass.viewOf(control);
        if (factorX !== 1) {
          if (!isBound(control, 'x')) control.x = Math.max(0, box.x + Math.round((view.x - box.x) * factorX));
          if (!isBound(control, 'w')) control.w = Math.max(GRID, Math.round(view.w * factorX));
        }
        if (factorY !== 1) {
          if (!isBound(control, 'y')) control.y = Math.max(0, box.y + Math.round((view.y - box.y) * factorY));
          if (!isBound(control, 'h')) control.h = Math.max(GRID, Math.round(view.h * factorY));
        }
        if (control.controls) scaleInside(control.controls);
      }
      renderCanvas();
      renderProperties();
      markDirty();
    }

    // ---- placing, moving and resizing ----

    // A drop lands in the innermost container under the pointer, so dropping onto a panel puts the
    // control inside it. Coordinates are always relative to the control's own parent.
    function dropTarget(clientX, clientY) {
      const element = document.elementFromPoint(clientX, clientY)?.closest('.gfd-container, .gfd-canvas');
      if (!element || !canvas.contains(element)) return null;
      const host = element.closest('.gfd-control');
      const list = host ? findControl(model.doc, host.dataset.id).controls : model.doc.controls;
      const bounds = element.getBoundingClientRect();
      return { list, originX: bounds.left, originY: bounds.top };
    }

    function placeControl(type, clientX, clientY) {
      const target = dropTarget(clientX, clientY) || {
        list: model.doc.controls,
        originX: canvas.getBoundingClientRect().left,
        originY: canvas.getBoundingClientRect().top,
      };
      let x = snap(clientX - target.originX);
      let y = snap(clientY - target.originY);
      // Placing without a drag (double-click, keyboard) aims at the same point every time, so
      // cascade off anything already sitting there rather than hiding it.
      while (target.list.some((c) => c.x === x && c.y === y)) {
        x += GRID * 2;
        y += GRID * 2;
      }

      const control = newControl(type, x, y);
      control.name = uniqueName(model.doc, type);
      target.list.push(control);
      markDirty();
      select(control.id);
    }

    // Ctrl (or Cmd) and Shift both mean "as well as what is already selected", because both are
    // what people arrive expecting and neither means anything else here.
    const adding = (event) => event.ctrlKey || event.metaKey || event.shiftKey;

    // Capture keeps the drag alive when the pointer leaves the canvas. It is worth having and not
    // worth failing over: a pointer that has already gone by the time this runs throws, and losing
    // the whole drag over that would be worse than dragging only while over the canvas.
    function capturePointer(event) {
      try { canvas.setPointerCapture(event.pointerId); } catch { /* drag on without it */ }
    }

    // The rubber band. Drawn in the canvas while the pointer is down on empty space, and gone
    // again the moment it is let go.
    function marqueeSelect(event) {
      const startX = event.clientX;
      const startY = event.clientY;
      const add = adding(event);
      const band = h('div', { class: 'gfd-marquee' });
      let dragging = false;

      event.preventDefault();
      capturePointer(event);

      const onMove = (moveEvent) => {
        // The band lasts as long as the button is down, for the same reason a drag does.
        if (moveEvent.buttons === 0) {
          onUp(moveEvent);
          return;
        }
        if (!dragging
          && Math.abs(moveEvent.clientX - startX) + Math.abs(moveEvent.clientY - startY) < 4) return;
        if (!dragging) {
          dragging = true;
          canvas.append(band);
        }
        const bounds = canvas.getBoundingClientRect();
        const left = Math.min(startX, moveEvent.clientX);
        const top = Math.min(startY, moveEvent.clientY);
        band.style.left = `${left - bounds.left}px`;
        band.style.top = `${top - bounds.top}px`;
        band.style.width = `${Math.abs(moveEvent.clientX - startX)}px`;
        band.style.height = `${Math.abs(moveEvent.clientY - startY)}px`;
      };

      const onUp = (upEvent) => {
        canvas.removeEventListener('pointermove', onMove);
        canvas.removeEventListener('pointerup', onUp);
        canvas.removeEventListener('pointercancel', onUp);
        if (!dragging) {
          // A click on empty space, not a drag: that is the component's own properties.
          if (!add) select(null);
          return;
        }
        band.remove();
        // Touching counts, not enclosing: a band you have to draw around every edge is a band you
        // redraw. Measured from the elements themselves, so a control inside a panel is caught by
        // where it appears rather than by coordinates relative to something else.
        const box = {
          left: Math.min(startX, upEvent.clientX), right: Math.max(startX, upEvent.clientX),
          top: Math.min(startY, upEvent.clientY), bottom: Math.max(startY, upEvent.clientY),
        };
        const hits = [...canvas.querySelectorAll('.gfd-control')].filter((element) => {
          const rect = element.getBoundingClientRect();
          return rect.left < box.right && rect.right > box.left
            && rect.top < box.bottom && rect.bottom > box.top;
        }).map((element) => element.dataset.id);
        selectAll(hits, add);
      };

      canvas.addEventListener('pointermove', onMove);
      canvas.addEventListener('pointerup', onUp);
      canvas.addEventListener('pointercancel', onUp);
    }

    canvas.addEventListener('pointerdown', (event) => {
      // Preview is the component, not a drawing of it: clicks belong to the controls.
      if (model.mode === 'preview') return;
      const handle = event.target.closest('.gfd-handle');
      const element = event.target.closest('.gfd-control');
      if (!element) {
        marqueeSelect(event);
        return;
      }

      const control = findControl(model.doc, element.dataset.id);
      if (!control) return;

      // Adding to the selection is a decision on its own, not the start of a drag: releasing on
      // the control you just added should leave it added rather than nudge it somewhere.
      if (adding(event)) {
        event.preventDefault();
        select(control.id, true);
        return;
      }

      // Selecting and moving are two acts, not one. A click on a control that is not selected only
      // selects it: the press that picks something out of a layout is usually not steady, and
      // moving on it meant nudging a control every time you went to look at one. Press it again to
      // move it, by which point moving is what you came to do.
      if (!isSelected(control.id)) {
        event.preventDefault();
        select(control.id);
        return;
      }

      const startX = event.clientX;
      const startY = event.clientY;
      const moving = handle ? [control] : movable();
      const start = new Map(moving.map((c) => [c.id, { x: c.x, y: c.y }]));
      const size = { w: control.w, h: control.h };
      const mode = handle ? handle.dataset.handle : 'move';
      let moved = false;

      event.preventDefault();
      capturePointer(event);

      // A coordinate driven by an expression is not the pointer's to change: writing to the
      // literal underneath it would move nothing and quietly edit a value nobody is reading.
      const onMove = (moveEvent) => {
        // A drag lasts exactly as long as the button is down. Losing the pointer — a capture taken
        // away, a release the canvas never saw because the window was not focused — used to leave
        // this listener attached, and the control then followed the bare pointer around with
        // nothing held down. The button state is on every move event, so ask it rather than trust
        // that the matching release will arrive.
        if (moveEvent.buttons === 0) {
          onUp();
          return;
        }
        const dx = moveEvent.clientX - startX;
        const dy = moveEvent.clientY - startY;
        if (!moved && Math.abs(dx) + Math.abs(dy) < 3) return;
        moved = true;
        if (mode === 'move') {
          // Every control shifts by the same snapped distance, so a group keeps its arrangement
          // instead of each part landing on the grid separately.
          const shiftX = snap(dx);
          const shiftY = snap(dy);
          for (const target of moving) {
            const from = start.get(target.id);
            if (!isBound(target, 'x')) target.x = Math.max(0, from.x + shiftX);
            if (!isBound(target, 'y')) target.y = Math.max(0, from.y + shiftY);
          }
        } else {
          if (mode.includes('e') && !isBound(control, 'w')) control.w = Math.max(GRID, snap(size.w + dx));
          if (mode.includes('s') && !isBound(control, 'h')) control.h = Math.max(GRID, snap(size.h + dy));
        }
        renderCanvas();
      };

      const onUp = () => {
        canvas.removeEventListener('pointermove', onMove);
        canvas.removeEventListener('pointerup', onUp);
        canvas.removeEventListener('pointercancel', onUp);
        canvas.removeEventListener('lostpointercapture', onUp);
        if (moved) {
          markDirty();
          renderProperties();
        }
        moved = false;
      };

      canvas.addEventListener('pointermove', onMove);
      canvas.addEventListener('pointerup', onUp);
      // A cancelled pointer and a capture taken elsewhere both end the drag as surely as a release
      // does, and neither of them sends one.
      canvas.addEventListener('pointercancel', onUp);
      canvas.addEventListener('lostpointercapture', onUp);
    });

    canvas.addEventListener('dragover', (event) => {
      if (event.dataTransfer.types.includes('application/x-gridlet-control')) event.preventDefault();
    });

    canvas.addEventListener('drop', (event) => {
      if (model.mode === 'preview') return;
      const type = event.dataTransfer.getData('application/x-gridlet-control');
      if (!type || !CATALOGUE[type]) return;
      event.preventDefault();
      placeControl(type, event.clientX, event.clientY);
    });

    canvas.addEventListener('keydown', (event) => {
      if (model.mode === 'preview') return;

      // Select every control in the component. The canvas has focus while designing, so this is the
      // keyboard's way to the same place the rubber band gets to.
      if ((event.ctrlKey || event.metaKey) && event.key.toLowerCase() === 'a') {
        const ids = [];
        walk(model.doc.controls, (control) => ids.push(control.id));
        selectAll(ids);
        event.preventDefault();
        return;
      }

      const controls = selectedControls();
      if (!controls.length) return;

      if (event.key === 'Delete') {
        deleteSelection();
        event.preventDefault();
        return;
      }

      const step = event.shiftKey ? GRID : 1;
      const nudge = { ArrowLeft: [-step, 0], ArrowRight: [step, 0], ArrowUp: [0, -step], ArrowDown: [0, step] }[event.key];
      if (!nudge) return;
      moveSelection(nudge[0], nudge[1]);
      event.preventDefault();
    });

    // ---- palette ----
    // The catalogue lives in the tab's own toolbar rather than in a sidebar, so the canvas keeps
    // the full width of the workspace. Each entry can be dragged to a spot or clicked to drop one
    // in the middle, which is also what makes the catalogue reachable without a pointer drag.

    const palette = h('div', { class: 'gfd-palette' },
      ...Object.entries(CATALOGUE).filter(([, spec]) => !spec.retired).map(([type, spec]) => {
        const item = h('button', {
          class: 'gfd-palette-item',
          type: 'button',
          draggable: 'true',
          title: `${spec.title} — drag onto the component, or click to add one`,
          'data-type': type,
          onclick: () => {
            const bounds = canvas.getBoundingClientRect();
            placeControl(type, bounds.left + bounds.width / 2, bounds.top + bounds.height / 2);
          },
        }, h('span', { class: 'gfd-palette-icon', text: spec.icon }), h('span', { text: spec.title }));
        item.addEventListener('dragstart', (event) => {
          event.dataTransfer.setData('application/x-gridlet-control', type);
          event.dataTransfer.effectAllowed = 'copy';
        });
        return item;
      }));

    // ---- saving ----

    async function saveComponent() {
      if (!model.name.trim()) {
        toast('Give the component a name before saving.');
        return;
      }
      saveButton.disabled = true;
      try {
        const savedComponent = await post('api/components', {
          id: model.id,
          name: model.name.trim(),
          schemaVersion: SCHEMA_VERSION,
          definition: model.doc,
        });
        model.id = savedComponent.id;
        tab.hasUnsavedDefinition = false;
        tab.title = savedComponent.name;
        refreshTabs();
        await sidebar.refresh();
        toast(`Saved ${savedComponent.name}.`, false);
      } catch (err) {
        saveButton.disabled = false;
        toast('Failed to save the component: ' + err.message);
      }
    }

    // Closing the tab is what loses the work, so that is when it is worth asking. Switching to
    // another tab — the module this component runs, most of all — leaves the component open exactly as it
    // was, and being asked to save on the way to the code is a question about nothing.
    tab.beforeClose = async () => {
      if (!tab.hasUnsavedDefinition) return true;
      return new Promise((resolve) => {
        modal('Unsaved component', h('p', { text: `${model.name} has unsaved changes.` }), [
          { label: 'Stay', onClick: (close) => { close(); resolve(false); } },
          { label: 'Discard', danger: true, onClick: (close) => { close(); resolve(true); } },
        ]);
      });
    };

    // ---- theme ----
    // A component ends up on a host page whose theme it does not control, so the theme here is a way of
    // checking the work in both, not something stored in the document. Auto follows the workspace.

    const THEMES = [
      { id: 'auto', label: 'Follow the workspace theme', icon: ICONS.contrast },
      { id: 'light', label: 'Light', icon: ICONS.sun },
      { id: 'dark', label: 'Dark', icon: ICONS.moon },
    ];

    const themeButtons = new Map(THEMES.map((definition) => [definition.id, h('button', {
      class: 'view-btn gfd-theme-btn',
      title: definition.label,
      'aria-label': definition.label,
      'data-testid': 'component-theme-' + definition.id,
      onclick: () => setTheme(definition.id, true),
    }, svgIcon(definition.icon, 'gfd-tab-icon'))]));

    function setTheme(theme, remember) {
      model.theme = theme;
      // The palette is defined for any element carrying data-theme, so themed variables cascade
      // into the component without the workspace around it changing.
      if (theme === 'auto') delete surface.dataset.theme;
      else surface.dataset.theme = theme;
      for (const [id, button] of themeButtons) {
        button.classList.toggle('active', id === theme);
        button.setAttribute('aria-pressed', String(id === theme));
      }
      if (remember) {
        try { localStorage.setItem('gridlet.components.theme', theme); } catch { /* unavailable */ }
        // The panel marks the colour column this theme shows, so it is redrawn with the component.
        renderProperties();
      }
    }

    // ---- grid switches ----
    // Two switches rather than one, because they answer different questions. The lines are about
    // reading the layout; the snapping is about what a drag is allowed to produce. Turning the
    // lines off while snapping stays on is a legitimate thing to want, and so is the reverse.

    const GRID_SWITCHES = [
      {
        id: 'grid',
        label: 'Show the placement grid',
        get: () => showGrid,
        set: (on) => { showGrid = on; },
        key: 'gridlet.components.grid',
        icon: ICONS['grid-3x3'],
      },
      {
        id: 'snap',
        label: 'Snap to the placement grid',
        get: () => snapToGrid,
        set: (on) => { snapToGrid = on; },
        key: 'gridlet.components.snap',
        icon: ICONS.magnet,
      },
    ];

    const gridButtons = GRID_SWITCHES.map((definition) => {
      const button = h('button', {
        class: 'view-btn gfd-theme-btn' + (definition.get() ? ' active' : ''),
        type: 'button',
        title: definition.label,
        'aria-label': definition.label,
        'aria-pressed': String(definition.get()),
        'data-testid': 'component-' + definition.id,
        onclick: () => {
          const on = !definition.get();
          definition.set(on);
          button.classList.toggle('active', on);
          button.setAttribute('aria-pressed', String(on));
          try { localStorage.setItem(definition.key, on ? '1' : '0'); } catch { /* unavailable */ }
          renderCanvas();
        },
      }, svgIcon(definition.icon, 'gfd-tab-icon'));
      return button;
    });

    const gridSwitcher = h('div',
      { class: 'view-switcher', role: 'group', 'aria-label': 'Placement grid' }, ...gridButtons);

    // ---- view switcher ----

    const viewButtons = ['design', 'preview'].map((mode) => h('button', {
      class: 'view-btn' + (mode === model.mode ? ' active' : ''),
      'data-testid': 'component-view-' + mode,
      'aria-pressed': String(mode === model.mode),
      onclick: () => setMode(mode),
    }, mode === 'design' ? 'Design' : 'Preview'));

    // Only GET endpoints are offered: a read-only component must not be able to invoke something that
    // writes just because it was the nearest match in a list.
    async function loadEndpoints() {
      try {
        const published = await api('api/published');
        model.endpoints = published.filter((e) =>
          e.enabled && String(e.method).toUpperCase() === 'GET');
      } catch (err) {
        toast('Failed to list published endpoints: ' + err.message);
      }
    }

    // Reading the source is how the designer learns both its column names and what a bound
    // property will actually show, so it is read while designing too rather than only in preview.
    // A failure at design time is usually a required parameter nobody has filled in yet, which is
    // not worth interrupting anyone over; a failure in preview is the component not working.
    async function loadRows(quiet = false) {
      model.rowIndex = 0;
      model.rows = [];
      model.columns = [];
      if (!model.doc.source) return;
      try {
        model.rows = await readSource(model.doc.source);
        if (model.rows.length) model.columns = Object.keys(model.rows[0]);
      } catch (err) {
        if (!quiet) toast(`The component's data source failed: ${err.message}`);
      }
    }

    function renderRecordBar() {
      const bound = Boolean(model.doc.source);
      // A component that carries its own pager does not want the designer's one above it as well: the
      // bar is the stand-in for a control the component has not got.
      let pager = false;
      walk(model.doc.controls, (control) => { if (control.type === 'pager') pager = true; });
      recordBar.hidden = !bound || model.mode !== 'preview' || pager;
      if (recordBar.hidden) return;
      recordPosition.textContent = model.rows.length
        ? `${model.rowIndex + 1} of ${model.rows.length}`
        : 'No records';
      previousButton.disabled = model.rowIndex <= 0;
      nextButton.disabled = model.rowIndex >= model.rows.length - 1;
    }

    function showRow(index) {
      model.rowIndex = Math.min(Math.max(0, index), Math.max(0, model.rows.length - 1));
      renderCanvas();
      renderRecordBar();
      emitComponentEvent('row', currentRow());
      if (model.mode === 'preview') runHandler(model.doc, 'row');
    }

    // ---- behaviour ----
    // A component's behaviour is ordinary JavaScript in ordinary modules. The document names the ones it
    // uses and holds no code and no event wiring of its own, so the layout stays something the
    // designer can draw and the behaviour stays something a developer can lint, diff and review.
    //
    // A module is handed one object — the component — and does whatever it likes with it. There is
    // nothing to import from Gridlet, nothing to register, and no framework to learn: a module is a
    // class that receives the component it belongs to.

    const behaviour = {
      instances: [],
      // Everything a module attached, so leaving the component takes it all away again and running it
      // twice cannot stack up two sets of listeners.
      listeners: [],
      handlers: new Map(),
      errors: [],
      // Exports that could not be offered under the name they were written with, said once beside
      // the modules they came from rather than at the expression that went looking for them.
      clashes: [],
      // The classes each loaded module exports by name, so the Behaviour section can offer them.
      classes: new Map(),
      // The second thing a class is handed: Gridlet's own services and any a module offers.
      services: null,
    };

    const moduleUrl = (name, version) =>
      `${WORKSPACE_ROOT}api/components/modules/${version}/${encodeURIComponent(name)}`;

    // A class is instantiated with the component; a plain function is called with it. Both are normal
    // ways to write a module, so both work rather than one being declared correct.
    const isClass = (value) => typeof value === 'function'
      && /^class[\s{]/.test(Function.prototype.toString.call(value));

    function recordBehaviourError(name, error) {
      const message = error?.message || String(error);
      behaviour.errors.push({ name, message });
      // Said once, and listed on the component's Behaviour section for as long as it is true. A toast
      // per failing row would bury the component it is complaining about.
      if (behaviour.errors.length === 1) toast(`${name}: ${message}`);
      renderProperties();
    }

    function emitComponentEvent(type, detail) {
      for (const handler of behaviour.handlers.get(type) || []) {
        try {
          handler(detail, componentApi);
        } catch (err) {
          recordBehaviourError('behaviour', err);
        }
      }
    }

    // Listeners are delegated from the canvas, because the canvas is redrawn whenever the row or
    // the document changes and a listener bound to an element would go with it. Capture is on, so
    // events that do not bubble — focus, blur — arrive as well.
    function delegate(match, type, handler) {
      const listener = (event) => {
        const box = event.target instanceof Element ? event.target.closest('.gfd-control') : null;
        if (!box || !match(box)) return;
        try {
          handler(event, componentApi);
        } catch (err) {
          recordBehaviourError('behaviour', err);
        }
      };
      canvas.addEventListener(type, listener, true);
      behaviour.listeners.push({ type, listener });
    }

    // A handle to a control by the name it has in the designer. Everything on it is resolved when
    // it is asked for, so a handle kept in a module stays valid across every redraw.
    function fieldHandle(name) {
      const box = () => canvas.querySelector(`[data-control-box="${quoted(name)}"]`);
      // The named element is the one you can see, so it is found directly rather than by walking
      // in from the box.
      const inner = () => canvas.querySelector(`[data-name="${quoted(name)}"]`);
      const control = () => {
        let found = null;
        walk(model.doc.controls, (candidate) => { if (candidate.name === name) found = candidate; });
        return found;
      };

      const handle = {
        get name() { return name; },
        get exists() { return Boolean(box()); },
        get element() { return box(); },
        get input() { return inner(); },

        get value() {
          const element = inner();
          if (!element) return undefined;
          if (element instanceof HTMLInputElement && element.type === 'checkbox') return element.checked;
          if ('value' in element && typeof element.value === 'string') return element.value;
          return element.textContent;
        },

        set value(next) {
          const element = inner();
          if (!element) return;
          const spec = CATALOGUE[control()?.type];
          if (spec?.bind) spec.bind(element, next);
          else element.textContent = asText(next);
        },

        get visible() { return box()?.style.display !== 'none'; },
        set visible(show) {
          const element = box();
          if (element) element.style.display = show ? '' : 'none';
        },

        get enabled() { return !inner()?.disabled; },
        set enabled(enable) {
          for (const element of box()?.querySelectorAll('input, textarea, select, button') || []) {
            element.disabled = !enable;
          }
        },

        on(type, handler) {
          delegate((element) => element.dataset.controlBox === name, type, handler);
          return handle;
        },

        focus() {
          inner()?.focus();
          return handle;
        },
      };
      return handle;
    }

    // What a module is given. Small on purpose: the fields, the data, the events, and a way to
    // reach the elements for anything this does not cover.
    const componentApi = {
      get name() { return model.name; },
      get element() { return canvas; },
      get mode() { return model.mode; },

      // The size the component is drawn at, which is what `component.width` answers in a formula. The two
      // are the same number from the same place, so a method and a formula agree about the component
      // they are both looking at.
      get width() { return pass ? pass.componentView().width : asNumber(model.doc.width); },
      get height() { return pass ? pass.componentView().height : asNumber(model.doc.height); },

      get fields() {
        const names = [];
        walk(model.doc.controls, (control) => { if (control.name) names.push(control.name); });
        return names;
      },

      field(name) { return fieldHandle(name); },

      get rows() { return model.rows; },
      get row() { return currentRow(); },
      get rowIndex() { return model.rowIndex; },
      get rowCount() { return model.rows.length; },

      goTo(index) { showRow(index); },
      next() { showRow(model.rowIndex + 1); },
      previous() { showRow(model.rowIndex - 1); },

      async reload() {
        await loadRows();
        renderCanvas();
        renderRecordBar();
        emitComponentEvent('load', model.rows);
        if (model.mode === 'preview') runHandler(model.doc, 'load');
      },

      // Component-level events: 'row' when the record changes, 'load' when rows are read again, and
      // whatever a module emits for another module to hear. Neither fires on startup — being
      // started is what connected() means, and a module that wants the first row reads component.row.
      on(type, handler) {
        if (!behaviour.handlers.has(type)) behaviour.handlers.set(type, new Set());
        behaviour.handlers.get(type).add(handler);
        return componentApi;
      },

      off(type, handler) {
        behaviour.handlers.get(type)?.delete(handler);
        return componentApi;
      },

      emit(type, detail) {
        emitComponentEvent(type, detail);
        return componentApi;
      },

      // Anything on any control, for the cases a field handle does not cover.
      query(selector) { return canvas.querySelector(selector); },
      queryAll(selector) { return [...canvas.querySelectorAll(selector)]; },

      notify(message) { toast(message, false); },
    };

    async function loadScripts() {
      try {
        model.scripts = await api('api/components/scripts');
      } catch (err) {
        model.scripts = [];
        toast('Failed to list modules: ' + err.message);
      }
    }

    // While this component is running it is reachable from the module tabs, so saving a module puts the
    // new version in front of whoever is looking at the component that uses it.
    const running = {
      usesModule: (name) => (model.doc.modules || []).some((entry) => moduleFileOf(entry) === name),
      restart: () => restartBehaviour(),
      refreshScope: () => refreshExpressionScope(),
    };

    async function stopBehaviour() {
      for (const { name, instance, connected } of behaviour.instances) {
        // Only what was connected is disconnected. A component being designed constructs its classes so
        // that their methods answer to a formula, and a class that was never started has nothing
        // to stop.
        if (!connected) continue;
        try {
          await instance.disconnected?.();
        } catch (err) {
          recordBehaviourError(name, err);
        }
      }
      behaviour.instances = [];
      for (const { type, listener } of behaviour.listeners) {
        canvas.removeEventListener(type, listener, true);
      }
      behaviour.listeners = [];
      behaviour.handlers.clear();
      resizeWatcher?.disconnect();
      resizeWatcher = null;
    }

    // ---- what an expression can call ----
    // A module's exports are ordinary names in an ordinary file, so an expression calls them by
    // those names and the module registers nothing: writing `export function vat(net)` is the whole
    // step. The modules offered are the ones this component names, which is also what tells a reader
    // where a name in an expression came from. A component can name as many as it likes, so a helper
    // lives in whichever file it belongs in; the names are merged in the order the component lists them.
    //
    // `default` and `setup` are how a module says what to *run*. Everything else it exports is
    // something to use: functions become calls, and any other value becomes a name, so
    // `export const VAT = 0.2` is written `data.Net * VAT`.

    let expressionScope = nameScope();
    // What the modules added, spelled the way they were exported. A name is matched without regard
    // to case, but it is written somewhere with a capital letter in it, and that is the spelling to
    // show back to the person who wrote it. Methods are listed with the class they belong to,
    // because that is the qualifier to write when two classes have a method of the same name.
    let expressionNames = { functions: [], values: [], methods: [] };

    const BEHAVIOUR_EXPORTS = new Set(['default', 'setup', 'services']);

    // What a class has because it is a class, or because it is this component's behaviour, rather than
    // because somebody wrote it to be called from a formula.
    const NOT_A_METHOD = new Set(['constructor', 'connected', 'disconnected']);

    // The name a module's own exports can be qualified by: the file without its extension.
    const stemOf = (name) => name.replace(/\.js$/i, '');

    // Returns how many names each module contributed, so a module that exports nothing usable and
    // runs nothing can be told from one that is pulled in purely for its helpers.
    function harvestExports(loaded) {
      const contributed = new Map();

      for (const { name, namespace } of loaded) {
        contributed.set(name, 0);
        const stem = stemOf(name);
        const qualifier = expressionScope.claim(stem, name) ? stem : null;
        if (!qualifier) reportQualifierClash(name, stem);

        for (const exported of Object.keys(namespace)) {
          if (BEHAVIOUR_EXPORTS.has(exported)) {
            // A module that offers nothing but services is a module worth naming, so what it
            // offers counts as its contribution even though no expression can name it.
            if (exported === 'services') contributed.set(name, contributed.get(name) + 1);
            continue;
          }
          // Two modules that import each other can leave a name declared but not yet given a value,
          // and reading it throws. One name is worth losing; the component is not.
          let value;
          try {
            value = namespace[exported];
          } catch (err) {
            behaviour.clashes.push({ name, message: `${exported} could not be read: ${err.message}` });
            continue;
          }

          // A class is behaviour, not a helper: it is offered on the Behaviour section to be run,
          // and calling one from a formula would only throw for want of `new`. It still counts as
          // something this module contributes, so a file written to hold two classes and nothing
          // else is not reported as a module with nothing in it.
          if (isClass(value)) {
            contributed.set(name, contributed.get(name) + 1);
            continue;
          }

          // Writing over a built-in is allowed — being unable to replace `json` with your own would
          // make the built-in a rule rather than a default — and it is also the kind of thing to
          // find out about from the component rather than from an expression that stopped doing what it
          // used to. Gridlet's own is still there, under the name of the library it came from.
          if (Object.hasOwn(FUNCTIONS, exported.toLowerCase())) {
            behaviour.clashes.push({
              name,
              message: `${exported} replaces Gridlet's own ${exported} everywhere in this component. `
                + `Write gridlet.${exported}() for Gridlet's.`,
            });
          }

          const kind = typeof value === 'function' ? 'function' : 'value';
          expressionScope.define(kind, exported, value, qualifier);
          contributed.set(name, contributed.get(name) + 1);
          expressionNames[kind === 'function' ? 'functions' : 'values'].push(exported);
        }
      }

      return contributed;
    }

    // Qualifiers share one namespace, because `tax.vat()` has to mean one thing. Whichever asked
    // second keeps its names — they are still callable when nothing else defines them — and loses
    // only the spelling that would have been ambiguous.
    function reportQualifierClash(name, label) {
      if (!QUALIFIER.test(label)) return;
      behaviour.clashes.push({
        name,
        message: `${label} is already the name of something else in this component, so ${label}.x() `
          + 'cannot reach this one. Rename one of them.',
      });
    }

    // ---- what a class is given beside the component ----
    // One explicit object, and the second argument, so a class written before there were services
    // is unaffected by there being some now. There is no naming convention and no decorator behind
    // it: a class asks for `services` or it does not, and what is in it is listed here.

    const stateKey = () => `gridlet.components.state.${model.id || 'new'}`;

    // Anything the workspace serves, and nothing else. A module reaching the wider web is writing
    // its own fetch and saying so; this one is the workspace's own API, already at the right root.
    const withinWorkspace = (path) => {
      const target = new URL(String(path), WORKSPACE_ROOT);
      if (target.origin !== location.origin || !target.href.startsWith(WORKSPACE_ROOT)) {
        throw new Error(`${path} is outside this workspace.`);
      }
      return target.href;
    };

    function gridletServices() {
      return {
        // The workspace's own message line, so a module does not have to draw one.
        notify: (message) => toast(message, false),

        http: {
          get: (path) => api(withinWorkspace(path)),
          post: (path, body) => api(withinWorkspace(path),
            { method: 'POST', body: JSON.stringify(body ?? null) }),
        },

        // Somewhere for a component to keep a little of its own state between visits, per component and per
        // browser. It is not the component's data and it is not saved with the document.
        storage: {
          read(key, fallback = null) {
            try {
              const held = JSON.parse(localStorage.getItem(stateKey()) || '{}');
              return Object.hasOwn(held, key) ? held[key] : fallback;
            } catch { return fallback; }
          },
          write(key, value) {
            try {
              const held = JSON.parse(localStorage.getItem(stateKey()) || '{}');
              held[key] = value;
              localStorage.setItem(stateKey(), JSON.stringify(held));
            } catch { /* storage unavailable, which is not worth stopping a component for */ }
          },
          clear() {
            try { localStorage.removeItem(stateKey()); } catch { /* unavailable */ }
          },
        },
      };
    }

    // A module may offer services of its own: `export const services = { audit: {...} }`. They are
    // for this component, so one module's service reaches every class the component runs. Two modules
    // offering the same name is the same question as two functions of the same name, and it gets
    // the same answer: neither is used, and the component says so.
    function buildServices(loaded) {
      const services = gridletServices();
      const own = new Set(Object.keys(services));
      const offered = new Map();

      for (const { name, namespace } of loaded) {
        let provided;
        try {
          provided = namespace.services;
        } catch (err) {
          behaviour.clashes.push({ name, message: `services could not be read: ${err.message}` });
          continue;
        }
        if (!provided || typeof provided !== 'object') continue;

        for (const [key, value] of Object.entries(provided)) {
          if (own.has(key)) {
            behaviour.clashes.push({
              name,
              message: `${key} is one of Gridlet's own services, so this one is not used. Rename it.`,
            });
            continue;
          }
          if (!offered.has(key)) offered.set(key, { owners: [name], value });
          else offered.get(key).owners.push(name);
        }
      }

      for (const [key, { owners, value }] of offered) {
        if (owners.length === 1) {
          services[key] = value;
          continue;
        }
        behaviour.clashes.push({
          name: key,
          message: `is offered as a service by ${owners.join(' and ')}, so none of them is used. `
            + 'Rename all but one.',
        });
      }

      return services;
    }

    // A module's class is this component's behaviour, and it is constructed while the component is being
    // designed as well as while it runs: its public methods are names a formula calls, and a name
    // that worked in Preview and not in Design would read as a bug in the component. What the class
    // does is still held back — the constructor stores what it is given, connected() acts on it,
    // and connected() belongs to a component that is running.
    // The entries this component holds for one file: the file itself, and any of its classes.
    const attachmentsOf = (name) =>
      (model.doc.modules || []).filter((entry) => moduleFileOf(entry) === name);

    // The classes a module offers by name, so the Behaviour section can list them under the file
    // they are written in. The default export is the file's own tick and is not listed twice.
    function discoverClasses(loaded) {
      behaviour.classes = new Map();
      for (const { name, namespace } of loaded) {
        const found = [];
        for (const exported of Object.keys(namespace)) {
          if (exported === 'default') continue;
          try {
            if (isClass(namespace[exported])) found.push(exported);
          } catch { /* a name declared but not yet given a value; the harvest says so */ }
        }
        behaviour.classes.set(name, found);
      }
    }

    function buildInstances(loaded, contributed) {
      for (const { name, namespace } of loaded) {
        for (const entry of attachmentsOf(name)) {
          const className = moduleClassOf(entry);
          try {
            const factory = className ? namespace[className] : namespace.default ?? namespace.setup;
            if (typeof factory !== 'function') {
              if (className) throw new Error(`${className} is not a class this module exports`);
              // A module named only for the functions it lends to expressions is a fair thing to
              // write, so having no behaviour is silence. Having neither is the mistake worth
              // saying.
              if (contributed.get(name)) continue;
              throw new Error('the module has no default export to run and exports nothing an expression can use');
            }
            const instance = (isClass(factory)
              ? new factory(componentApi, behaviour.services)
              : factory(componentApi, behaviour.services)) || {};
            behaviour.instances.push({ name, instance, connected: false });
            harvestMethods(name, factory, instance);
          } catch (err) {
            recordBehaviourError(name, err);
          }
        }
      }
    }

    // The public methods of a behaviour class, offered as names a formula can call. Each is bound
    // to the instance it belongs to, so `this` inside it is the object that was constructed with
    // the component. A #private method is not on the prototype at all, so keeping something to yourself
    // is the ordinary JavaScript way of doing it and nothing here has to know about it.
    function harvestMethods(name, factory, instance) {
      const prototype = Object.getPrototypeOf(instance);
      if (!prototype || prototype === Object.prototype) return;
      const members = Object.getOwnPropertyNames(prototype).filter((member) => {
        if (NOT_A_METHOD.has(member)) return false;
        // A method, not a getter: reading a getter means running it, and a component drawing itself is
        // not asking for that.
        return typeof Object.getOwnPropertyDescriptor(prototype, member)?.value === 'function';
      });
      if (!members.length) return;

      const label = factory.name || stemOf(name);
      const qualifier = expressionScope.claim(label, name) ? label : null;
      if (!qualifier) reportQualifierClash(name, label);

      for (const member of members) {
        const method = Object.getOwnPropertyDescriptor(prototype, member).value.bind(instance);
        expressionScope.define('function', member, method, qualifier);
        expressionNames.methods.push(qualifier ? `${qualifier}.${member}` : member);
      }
    }

    // One name meaning two things is said once, beside the modules it came from, as well as by the
    // formula that went looking for it.
    function reportAmbiguities() {
      for (const { spelling, written } of expressionScope.ambiguities()) {
        behaviour.clashes.push({
          name: spelling,
          message: written
            ? `is defined more than once in this component, so a formula has to write ${written}.`
            : 'is defined more than once in this component. Rename one of them.',
        });
      }
    }

    // The one place the component's scope is built, because Design and Preview have to agree about what
    // a name means. Preview adds what running means on top of it: connected(), and the handlers.
    async function rebuildScope() {
      expressionScope = nameScope();
      expressionNames = { functions: [], values: [], methods: [] };
      behaviour.clashes = [];
      behaviour.errors = [];
      const loaded = await loadModules();
      discoverClasses(loaded);
      const contributed = harvestExports(loaded);
      behaviour.services = buildServices(loaded);
      buildInstances(loaded, contributed);
      reportAmbiguities();
    }

    // Loaded fresh every time. The version in the URL is what gets past the browser's module cache,
    // and because relative imports resolve beside the module they carry the same version — so a
    // shared module that has just been edited is re-read too.
    async function loadModules() {
      // One import per file: a component that runs two classes out of one module reads that module once,
      // and the two share whatever the file itself keeps.
      const names = [...new Set((model.doc.modules || []).map(moduleFileOf))].filter(Boolean);
      const loaded = [];
      if (!names.length) return loaded;

      const version = Date.now();
      for (const name of names) {
        try {
          loaded.push({ name, namespace: await import(moduleUrl(name, version)) });
        } catch (err) {
          recordBehaviourError(name, err);
        }
      }
      return loaded;
    }

    // Modules are read whenever the component's list of them changes, not only when the component runs: the
    // canvas evaluates expressions every time it is drawn, and a name that worked in Preview and
    // broke in Design would read as a bug in the component. Importing a module evaluates the file; it
    // does not construct its default export, so behaviour still starts only when the component does.
    async function refreshExpressionScope() {
      await stopBehaviour();
      await rebuildScope();
      renderCanvas();
      renderProperties();
    }

    // ---- handlers ----
    // A handler is a formula run for what it does rather than for what it returns. It names the
    // same things a property's formula names — the row, the controls, the component — and it calls the
    // same functions, so there is one language on the component and not two.
    //
    // A handler's function is called exactly like any other: with no `this`, and with what it needs
    // passed to it. `=showPrice(component, data.Price)` says what it is given, on the control it belongs
    // to, and the same function works from any other control without being rewritten.

    const handlerName = (target) =>
      (target === model.doc ? model.name || 'component' : target.name || target.type);

    function runHandler(target, event) {
      const formula = target.events?.[event];
      if (!isFormula(formula)) return;
      const body = formulaBody(formula).trim();
      if (!body) return;
      try {
        const result = evaluate(body, pass.lookupFor(target), expressionScope);
        // A handler is run for its effect, so its answer is only interesting when it is an error.
        if (isError(result)) {
          recordBehaviourError(handlerName(target), `${event}: ${result.detail || result.code}`);
        }
      } catch (err) {
        recordBehaviourError(handlerName(target), `${event}: ${err.message}`);
      }
    }

    const controlNamed = (name) => {
      let found = null;
      walk(model.doc.controls, (candidate) => { if (candidate.name === name) found = candidate; });
      return found;
    };

    // One delegated listener per event kind for the whole canvas, matching the way a module's own
    // listeners are attached: the canvas is redrawn constantly and a listener on a control would go
    // with it.
    function attachHandlers() {
      for (const [event] of CONTROL_EVENTS) {
        delegate(() => true, event, (nativeEvent) => {
          const box = nativeEvent.target instanceof Element
            ? nativeEvent.target.closest('.gfd-control') : null;
          const control = box?.dataset.controlBox ? controlNamed(box.dataset.controlBox) : null;
          if (control) runHandler(control, event);
        });
      }

      // The component's own size, watched rather than polled. It goes when the component stops running.
      if (model.doc.events?.resize) {
        resizeWatcher = new ResizeObserver(() => runHandler(model.doc, 'resize'));
        resizeWatcher.observe(canvas);
      }
    }

    let resizeWatcher = null;

    async function startBehaviour() {
      await stopBehaviour();
      await rebuildScope();

      for (const record of behaviour.instances) {
        try {
          // Marked as started before it is, because a connected() that threw half way through has
          // taken whatever it took and disconnected() is the only thing that gives it back.
          record.connected = true;
          await record.instance.connected?.();
        } catch (err) {
          recordBehaviourError(record.name, err);
        }
      }

      attachHandlers();
    }

    async function setMode(mode) {
      if (model.mode === mode) return;
      model.mode = mode;
      // Leaving design clears the selection: a selected control is a designer concept, and coming
      // back with stale handles drawn over a component you were just filling in reads as a glitch.
      if (mode === 'preview') model.selection = [];
      for (const [index, button] of viewButtons.entries()) {
        const active = (index === 0) === (mode === 'design');
        button.classList.toggle('active', active);
        button.setAttribute('aria-pressed', String(active));
      }
      palette.hidden = mode === 'preview';
      // Both switches are about drawing the component. Preview has no grid to show and nothing to
      // snap, so offering them there would be offering a control that does nothing.
      gridSwitcher.hidden = mode === 'preview';
      designer.classList.toggle('previewing', mode === 'preview');
      renderCanvas();
      renderProperties();
      if (mode === 'preview') await loadRows();
      renderCanvas();
      renderRecordBar();

      // Behaviour runs when the component does. Design is for drawing it, and a module reaching into a
      // half-drawn layout would be answering questions about a component that is still being built.
      if (mode === 'preview') {
        runningComponents.add(running);
        await startBehaviour();
        // The component is running and its rows have arrived, which is what On load is named for.
        runHandler(model.doc, 'load');
      } else {
        runningComponents.delete(running);
        // Stopped, and then built again without being started: back in Design the classes are here
        // for what a formula calls on them and for nothing else.
        await refreshExpressionScope();
      }
      renderProperties();
    }

    // Running the modules again without leaving preview, for the edit-and-see-it loop the code
    // pane is for.
    async function restartBehaviour() {
      if (model.mode !== 'preview') {
        await setMode('preview');
        return;
      }
      renderCanvas();
      await startBehaviour();
    }

    // ---- record navigation ----

    const recordPosition = h('span', { class: 'gfd-record-position', 'data-testid': 'component-record-position' });

    const previousButton = h('button', {
      class: 'ghost', title: 'Previous record', 'data-testid': 'component-record-previous',
      onclick: () => showRow(model.rowIndex - 1),
    }, '‹');

    const nextButton = h('button', {
      class: 'ghost', title: 'Next record', 'data-testid': 'component-record-next',
      onclick: () => showRow(model.rowIndex + 1),
    }, '›');

    const recordBar = h('div', { class: 'gfd-record-bar', hidden: '' },
      h('button', {
        class: 'ghost', title: 'First record',
        onclick: () => showRow(0),
      }, '«'),
      previousButton,
      recordPosition,
      nextButton,
      h('button', {
        class: 'ghost', title: 'Last record',
        onclick: () => showRow(model.rows.length - 1),
      }, '»'),
      h('span', { class: 'spacer' }),
      h('span', { class: 'muted gfd-readonly-note', text: 'Read-only' }));

    // ---- binding help ----
    // The reference for the expression language, written where someone meets it. The function list
    // is read from the table that implements it, so it cannot drift from what actually works.

    // What this component's own modules add, listed the same way and from the same tables, so a name a
    // module exports is as easy to find as a built-in one.
    function moduleHelp() {
      const added = expressionNames.functions;
      const values = expressionNames.values;
      const methods = expressionNames.methods;
      if (!added.length && !values.length && !methods.length) return [];

      const line = (label, names) => (names.length
        ? [h('p', { class: 'gfd-help-functions', text: `${label}: ${[...names].sort().join(', ')}` })]
        : []);

      return [
        ...line('From this component\'s modules', added),
        ...line('Methods of the classes this component runs', methods),
        ...line('Values from this component\'s modules', values),
        h('p', { class: 'field-note' },
          'Anything a module this component runs exports, apart from its default, can be named here. '
          + 'Export a function to call it; export any other value to use it as a name. A method is '
          + 'listed with the class it belongs to, and either spelling calls it while nothing else '
          + 'in this component has that name.'),
      ];
    }

    function showBindingHelp() {
      const term = (code, description) => h('div', { class: 'gfd-help-row' },
        h('code', { text: code }),
        h('span', { text: description }));

      const group = (title, ...rows) => h('div', { class: 'gfd-help-group' },
        h('h4', { text: title }), ...rows);

      modal('Binding a property', h('div', { class: 'gfd-help-body' },
        h('p', { text: 'Every property in this panel can hold an expression instead of a fixed value. Click the ƒ beside it and write one: the property then follows whatever the expression names, live, in Design and in Preview. Click ƒ again to go back to a fixed value — whatever the expression last worked out to is kept, so nothing jumps.' }),

        group('What an expression can name',
          term('data.Email', 'A column of the component\'s data source, on the row being shown. Write data["Order Date"] for a name with a space in it.'),
          term('data', 'The whole row. A row is an object, and an object shown as text is JSON — turn Multiline on for a text box to hold it.'),
          term('component.rows', 'Every row the source returned, again as JSON.'),
          term('self.h', 'Another property of this same control: x, y, w, h, and the control\'s own properties.'),
          term('button1.w', 'The same, on any control in the component, by the name it carries on its Settings page.'),
          term('button1.right', 'Derived edges of a control: right, bottom, centreX, centreY.'),
          term('button1.tip', 'What a control carries as well: tip, classes, elementId, name, type.'),
          term('component.width', 'The component itself: width, height, name, row, rowCount.'),
          term('component', 'The whole component, to hand to a function of your own: showSize(component).')),

        group('Operators',
          term('+ - * / %', 'Arithmetic. + joins text instead when either side is not a number.'),
          term('== != < <= > >=', 'Comparison.'),
          term('&& || !', 'And, or, not. Empty text, 0 and "false" all count as false.'),
          term('test ? a : b', 'Choose between two values.')),

        group('Functions',
          h('p', { class: 'gfd-help-functions', text: Object.keys(FUNCTIONS).sort().join(', ') }),
          h('p', { class: 'field-note' },
            'They are written in gridlet.js, which the Code section lists and any module can import. '
            + 'What you read there is what runs your expression.'),
          ...moduleHelp()),

        group('Naming a function exactly',
          term('vat(100)', 'A function this component\'s modules export, or a method of a class it runs, while only one thing in this component has that name.'),
          term('Tax.vat(100)', 'The method of the class called Tax.'),
          term('tax.vat(100)', 'What tax.js exports, by the name of the file it is written in.'),
          term('gridlet.json(data)', 'Gridlet\'s own, which stays reachable when a module writes over the name.'),
          term('tax.VAT_RATE', 'A value a module exports, qualified the same way. Without the brackets it is a name, not a call.'),
          h('p', { class: 'field-note' },
            'Two of your own definitions under one name are not ranked against each other: the bare '
            + 'name answers #NAME? and says which spellings reach which. The component\'s Settings page '
            + 'lists them beside the modules they came from.')),

        group('When a formula fails',
          term('#NAME?', 'No function or name of that name. Check the spelling.'),
          term('#VALUE!', 'Arithmetic on something that is not a number, or a module\'s function threw.'),
          term('#DIV/0!', 'A division by zero.'),
          term('#NUM!', 'The result is not a number a component can use.'),
          term('#CIRC!', 'The formula needs its own value.'),
          term('#SYNTAX?', 'The formula cannot be read at all.'),
          term('iferror(x, 0)', 'Your own answer to a failure, in place of the code.'),
          h('p', { class: 'field-note' },
            'An error travels: anything built on #VALUE! is #VALUE! too. A property that has no room '
            + 'for a code — a position, a size, a tick — keeps the value it last worked out to, and '
            + 'the code is shown here beside the formula.')),

        group('For example',
          term('data.FirstName + " " + data.LastName', 'Two columns in one label.'),
          term('self.h', 'A square control: its width follows its own height.'),
          term('button1.right + 8', 'Sit eight pixels to the right of button1, and stay there.'),
          term('if(data.LoyaltyPoints > 100, "#ffd479", "")', 'A fill that depends on the data.'),
          term('concat("Row ", component.row, " of ", component.rowCount)', 'A caption that counts.'),
          term('json(data, 2)', 'The whole row as indented JSON, in a multiline text box.')),

        group('When an expression is not enough',
          term('Code', 'Modules are listed in the sidebar and open in their own tab. A module is ordinary JavaScript — import, export, classes, #private fields — and a component runs the ones it names on its Settings page.'),
          term('constructor(component, services)', 'What a module is given. A class is instantiated with the component; a plain default-exported function is called with it. The second argument is optional and holds services.notify, services.http.get / .post inside this workspace, and services.storage.read / .write / .clear for a little state per component.'),
          term('export const services', 'A module can offer services of its own — { audit: { note(what) {} } } — and every class this component runs is handed them. Two modules offering one name is a clash, said on this component, and neither is used.'),
          term('export class Two', 'A module can hold several classes. The component\'s Settings page lists them under the file and each has its own tick, so one file can carry the behaviour of more than one component.'),
          term('connected()', 'Called once the component is running and its rows have loaded. disconnected() is called when it stops.'),
          term('component.field(name)', 'A control by its designer name: .value, .enabled, .visible, .focus(), .element, and .on(type, handler) for its DOM events.'),
          term('component.on("row")', 'The record changed. Also "load" after component.reload(), and anything a module sends with component.emit().'),
          term('component.row / component.rows', 'The row on screen and every row the source returned. component.goTo(i), next(), previous() and reload() move around them.')),

        h('p', { class: 'field-note' },
          'Several controls can be selected at once — drag a box around them, or hold Ctrl or Shift '
          + 'while clicking — and anything set here is set on all of them. '
          + 'A coordinate driven by an expression cannot be dragged or nudged: the expression decides it. '
          + 'A box marked in red could not be worked out — rest the pointer on it for the reason, '
          + 'and the property falls back to its fixed value until it is put right.')),
        [{ label: 'Close', primary: true, onClick: (close) => close() }]);
    }

    // ---- properties rail ----

    // The object sidebar's control, mirrored. Same markup, same classes, same behaviour: one
    // button that collapses and expands, on a rail that is clickable as a whole once collapsed.
    const railToggle = h('button', {
      class: 'ghost panel-toggle gfd-rail-toggle',
      title: 'Collapse the properties panel',
      'aria-label': 'Collapse the properties panel',
      onclick: () => setRail(!rail.classList.contains('collapsed'), true),
      // The panel is on the right, and the set has that icon, so the button no longer wears a
      // mirrored copy of the left-hand one.
    }, svgIcon(ICONS['layout-sidebar-right'], 'panel-toggle-icon'));

    const subjectLabel = h('span', { class: 'gfd-subject-kind' });
    const subjectName = h('span', { class: 'gfd-subject-name' });

    // The whole expression language, one click from every row that can take one. A tooltip cannot
    // hold a reference, and a reference nobody can find is one nobody uses.
    const helpButton = h('button', {
      class: 'ghost gfd-help',
      type: 'button',
      title: 'How binding works — the ƒ beside a property',
      'aria-label': 'How binding works',
      'data-testid': 'binding-help',
      onclick: showBindingHelp,
    }, 'i');

    // Icon-only tabs. Each is one page of the panel for whatever is selected, so the same three
    // headings apply to the component and to every control rather than the panel changing shape.
    const tabButtons = new Map(TABS.map((definition) => [definition.id, h('button', {
      // The same segmented control as the Design and Preview toggle, with no overrides: the two
      // switchers in this tab are the same control doing the same job.
      class: 'view-btn',
      role: 'tab',
      title: definition.label,
      'aria-label': definition.label,
      'data-testid': 'properties-tab-' + definition.id,
      onclick: () => {
        model.tab = definition.id;
        try { localStorage.setItem('gridlet.components.tab', definition.id); } catch { /* unavailable */ }
        renderProperties();
      },
    }, svgIcon(definition.icon))]));

    const railGrip = h('div', {
      class: 'gfd-rail-grip',
      role: 'separator',
      'aria-orientation': 'vertical',
      'aria-label': 'Resize the properties panel',
    });

    // The toggle is a direct child so the shell's shared rail rule can keep it while hiding
    // everything else, exactly as it does for the object sidebar.
    const rail = h('div', { class: 'gfd-properties side-rail' },
      railGrip,
      h('div', { class: 'rail-head gfd-rail-header' },
        h('div', { class: 'gfd-subject' }, subjectLabel, subjectName),
        helpButton,
        railToggle),
      h('div', { class: 'view-switcher gfd-tabs', role: 'tablist' }, ...tabButtons.values()),
      propertyBody);

    // Pinned across components and reloads: someone who works with the rail closed wants it closed.
    function setRail(collapsed, remember) {
      rail.classList.toggle('collapsed', collapsed);
      const label = collapsed ? 'Expand the properties panel' : 'Collapse the properties panel';
      railToggle.title = label;
      railToggle.setAttribute('aria-label', label);
      railToggle.setAttribute('aria-expanded', String(!collapsed));
      if (remember) {
        try { localStorage.setItem('gridlet.components.railCollapsed', collapsed ? '1' : '0'); } catch { /* unavailable */ }
      }
    }

    // Collapsed, the whole rail is the way back — the same bargain the object sidebar makes.
    rail.addEventListener('click', (event) => {
      if (event.target.closest('.gfd-rail-toggle')) return;
      if (rail.classList.contains('collapsed')) setRail(false, true);
    });

    function setRailWidth(width, remember) {
      const next = Math.min(Math.max(220, width), 520);
      rail.style.setProperty('--gfd-rail-width', next + 'px');
      if (remember) {
        try { localStorage.setItem('gridlet.components.railWidth', String(next)); } catch { /* unavailable */ }
      }
    }

    railGrip.addEventListener('pointerdown', (event) => {
      if (rail.classList.contains('collapsed')) return;
      event.preventDefault();
      event.stopPropagation();
      railGrip.setPointerCapture(event.pointerId);
      railGrip.classList.add('dragging');
      const startX = event.clientX;
      const startWidth = rail.offsetWidth;
      // The rail is on the right, so dragging left widens it.
      const move = (moveEvent) => setRailWidth(startWidth - (moveEvent.clientX - startX), false);
      const stop = () => {
        railGrip.removeEventListener('pointermove', move);
        railGrip.removeEventListener('pointerup', stop);
        railGrip.removeEventListener('pointercancel', stop);
        railGrip.classList.remove('dragging');
        setRailWidth(rail.offsetWidth, true);
      };
      railGrip.addEventListener('pointermove', move);
      railGrip.addEventListener('pointerup', stop);
      railGrip.addEventListener('pointercancel', stop);
    });

    const designer = h('div', { class: 'gfd-designer' },
      h('div', { class: 'gfd-main' },
        h('div', { class: 'viewbar gfd-viewbar' },
          saveButton,
          h('div', { class: 'view-switcher', role: 'group', 'aria-label': 'Component view' }, ...viewButtons),
          h('div', { class: 'view-switcher', role: 'group', 'aria-label': 'Component theme' }, ...themeButtons.values()),
          gridSwitcher,
          palette),
        recordBar,
        surface),
      rail);

    panel.append(defaultStyle, generatedStyle, customStyle, designer);

    // Following the workspace theme means following it here too: the panel marks the colour column
    // that is on screen, and the workspace switch is a plain attribute change with no event behind
    // it. The observer goes when the tab does.
    const themeWatcher = new MutationObserver(() => {
      if (model.theme === 'auto') renderProperties();
    });
    themeWatcher.observe(document.documentElement, {
      attributes: true,
      attributeFilter: ['data-theme'],
    });
    openComponents.add(running);
    tab.onClose = () => {
      themeWatcher.disconnect();
      runningComponents.delete(running);
      openComponents.delete(running);
      // The stylesheet tabs edit this document, so they close with the component they belong to.
      closeCssTabs();
      // A module may have taken a timer or a listener outside the component; closing the tab is its cue
      // to give them back.
      return stopBehaviour();
    };

    setTheme(readStored('gridlet.components.theme', 'auto'), false);
    setRail(readStored('gridlet.components.railCollapsed', '0') === '1', false);
    setRailWidth(Number(readStored('gridlet.components.railWidth', '280')) || 280, false);

    surface.dataset.gfdScope = newId();
    renderCanvas();
    renderProperties();

    loadEndpoints()
      .then(() => loadRows(true))
      .then(() => { renderProperties(); renderCanvas(); });

    // The workspace's modules, so the Settings page can offer them. Listed whether or not anybody
    // opens a code tab, because a component that runs one has to run it either way.
    loadScripts().then(() => renderProperties());

    // What the component's own modules export, ready before the first expression asks for it.
    refreshExpressionScope();

    // Stylesheet tabs this component had open before the page was reloaded. They were waiting for the
    // component to build itself, which has just happened.
    for (const target of pendingCssTabs.get(model.id) || []) {
      const control = target === '@component' ? model.doc : findControl(model.doc, target);
      if (control) openCssTab(control);
    }
    pendingCssTabs.delete(model.id);
  }

  // ---- sidebar section --------------------------------------------------------
  // Components are listed beside tables and views, because they are another kind of thing you open.

  let components = [];

  const sidebar = registerSidebarSection({
    id: 'components',
    label: 'Components',
    badge: 'C',
    createTitle: 'Create component',
    onCreate: () => openDesigner(null),
    load: async () => { components = await api('api/components'); },
    items: () => components.map((component) => ({
      name: component.name,
      title: `${component.name} — updated ${new Date(component.updatedAtUtc).toLocaleString()}`,
      onOpen: () => openDesigner(component),
      contextItems: () => [
        { label: 'Open', action: () => openDesigner(component) },
        { separator: true },
        {
          label: 'Delete component…',
          danger: true,
          action: () => confirmModal('Delete component',
            `Delete ${component.name}? This cannot be undone.`,
            async () => {
              await del('api/components/' + encodeURIComponent(component.id));
              await sidebar.refresh();
            }),
        },
      ],
    })),
  });

  // Modules are listed beside the components, because a module is a thing you open and work on rather
  // than a setting inside something else. Any component can name any of them.
  let scripts = [];

  const codeSidebar = registerSidebarSection({
    id: 'component-scripts',
    label: 'Code',
    badge: 'JS',
    createTitle: 'New module',
    onCreate: () => newModule(async (script) => {
      await codeSidebar.refresh();
      openCodeTab(script.name);
    }),
    load: async () => { scripts = await scriptApi.list(); },
    items: () => scripts.map((script) => ({
      name: script.name,
      title: script.readOnly
        ? `${script.name} — part of Gridlet, and importable from your own modules`
        : `${script.name} — updated ${new Date(script.updatedAtUtc).toLocaleString()}`,
      onOpen: () => openCodeTab(script.name),
      contextItems: () => [
        { label: 'Open', action: () => openCodeTab(script.name) },
        // Gridlet's own modules have no delete: they are part of the build.
        ...(script.readOnly ? [] : [
          { separator: true },
          {
            label: 'Delete module…',
            danger: true,
            action: () => confirmModal('Delete module',
              `Delete ${script.name}? Any component that runs it will stop doing so.`,
              async () => {
                await scriptApi.remove(script.name);
                await codeSidebar.refresh();
              }),
          },
        ]),
      ],
    })),
  });

})();
