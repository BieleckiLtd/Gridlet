// The component document, as HTML.
//
// A component used to be stored as a JSON dialect that the designer converted to HTML on every
// draw. The dialect had to be written down, versioned, migrated and learned, and the thing it
// described was HTML the whole time: a label is a `<span>`, a text box is an `<input>`, a
// drop-down is a `<select>` holding `<option>`s. So the document is HTML now, and the conversion
// that used to happen on every draw happens once, here, when a document is read and written.
//
// What the format is trying to be: a file somebody who knows HTML can open, read, diff in review
// and edit by hand without knowing anything about Gridlet. Everything that has an HTML spelling
// uses it — `id`, `class`, `title`, `placeholder`, `readonly`, the element's own text. Only what
// HTML has no word for is a `data-` attribute, and those are named so their meaning is legible:
// `data-bind-text`, `data-on-click`, `data-fill-dark`.
//
// What the format deliberately is not: executable. Handlers are `data-on-click`, never `onclick`,
// and no `<script>` is written or read. A component document stays a description of a component;
// the code it runs lives in the module files it names. That separation is what lets a document be
// stored, copied between environments and rendered without the document itself being a thing that
// runs — see the note on modules in GridletComponentScript.cs.

(() => {
  'use strict';

  const SCHEMA = 2;

  // The kinds, by the markup each one is. A kind is recognised on the way in by its tag and, where
  // one tag serves two kinds, by `data-role`. Everything else about a control is read off the
  // element the way a browser would read it, which is the point of the format.
  const KINDS = {
    label: { tag: 'span' },
    textbox: { tag: 'input' },       // or `textarea` when multiline
    textarea: { tag: 'textarea', role: 'textarea' },
    checkbox: { tag: 'label', role: 'checkbox' },
    select: { tag: 'select' },
    button: { tag: 'button' },
    pager: { tag: 'div', role: 'pager' },
    panel: { tag: 'div', role: 'panel', container: true },
    // Markup the designer has no control for: a `<p>`, a `<table>`, an `<svg>` somebody wrote by
    // hand. It is kept exactly as it was written and drawn where it was put, because a document is
    // a file people edit and dropping the parts Gridlet did not recognise would quietly delete
    // their work. It is `locked` because the designer can render it but cannot claim to know what
    // its properties are, and a panel that offered to edit it would be guessing.
    foreign: { locked: true },
  };

  // Geometry is an inline style because that is where a reader looks for it, and because the four
  // numbers mean the same in the file as they do in a browser. The renderer still lifts them into
  // the generated stylesheet rather than leaving them inline: an inline custom property beats every
  // stylesheet rule, so a component's own CSS could not redefine them if they stayed here.
  const GEOMETRY = [['left', 'x'], ['top', 'y'], ['width', 'w'], ['height', 'h']];

  // Colours are held per theme, so each is its own attribute rather than one attribute with a
  // syntax of its own to learn. Empty means "leave the default alone" for that theme.
  const COLORS = [
    ['data-color-light', 'light', 'text'],
    ['data-color-dark', 'dark', 'text'],
    ['data-fill-light', 'light', 'background'],
    ['data-fill-dark', 'dark', 'background'],
  ];

  const px = (value) => `${Math.round(Number(value) || 0)}px`;
  const unpx = (value) => Math.round(parseFloat(value) || 0);

  // ---- writing --------------------------------------------------------------

  function toHtml(doc, name) {
    const root = document.createElement('form');
    root.setAttribute('data-gridlet', String(SCHEMA));
    root.setAttribute('data-name', name || 'component');
    root.setAttribute('data-layout', doc.layout || 'free');
    root.style.width = px(doc.width);
    root.style.height = px(doc.height);

    // Only what is on gets written. A document full of `data-isolated="false"` reads as a list of
    // settings; a document that mentions isolation only when it is isolated reads as a component.
    if (doc.isolated) root.setAttribute('data-isolated', '');
    if (doc.showScrollbars) root.setAttribute('data-scrollbars', '');
    if (doc.resizable) root.setAttribute('data-resizable', '');
    if (doc.elementId) root.id = doc.elementId;
    if (doc.classes && doc.classes.trim()) root.className = doc.classes.trim();
    if (doc.tip) root.title = doc.tip;

    writeColors(root, doc.colors);
    writeBindings(root, doc.bind);
    writeHandlers(root, doc.events);

    // A module is named, never inlined. `rel` says what the link is for, `href` is the file name
    // the component imports it under, and a browser ignores the whole element — which is correct,
    // because loading these is the runtime's job and not the document's.
    // A module may be named on its own, or with one of the classes it exports: naming the class is
    // what says which of them the component runs, and a module holding two is the reason that is a
    // separate thing to say rather than being implied by the file.
    for (const module of doc.modules || []) {
      const link = document.createElement('link');
      link.setAttribute('rel', 'gridlet-module');
      link.setAttribute('href', typeof module === 'string' ? module : (module.module || ''));
      if (typeof module !== 'string' && module.class) {
        link.setAttribute('data-class', module.class);
      }
      root.append(link);
    }

    // The data source is a published route and the parameters handed to it. The connection and the
    // SQL behind that route stay on the server: a document is readable by whoever opens the
    // component, so nothing about the database belongs in it.
    //
    // Each parameter is a `<param>` rather than an attribute named after it. A parameter's name is
    // the one the endpoint declared, and attribute names are lower-cased by every HTML parser, so a
    // name that arrived as `MinAge` would not survive being written and read back. As a value it
    // does.
    if (doc.source && doc.source.route) {
      const source = document.createElement('gridlet-source');
      source.setAttribute('href', doc.source.route);
      for (const [key, value] of Object.entries(doc.source.parameters || {})) {
        if (value !== '' && value !== null && value !== undefined) {
          const parameter = document.createElement('param');
          parameter.setAttribute('name', key);
          parameter.setAttribute('value', String(value));
          source.append(parameter);
        }
      }
      root.append(source);
    }

    const styles = styleSheetFor(doc);
    if (styles) {
      const style = document.createElement('style');
      style.textContent = styles;
      root.append(style);
    }

    for (const control of doc.controls || []) root.append(writeControl(control));
    return serialize(root);
  }

  // The component's CSS and every control's CSS, in one stylesheet, because that is what a
  // stylesheet is. A control's own rules stay findable rather than being merged away: the marker
  // comment says whose they are, so opening the file and editing them still works, and reading them
  // back does not have to guess from a selector the author was free to change.
  function styleSheetFor(doc) {
    const sections = [];
    if (doc.css && doc.css.trim()) sections.push(doc.css.trim());
    walk(doc.controls || [], (control) => {
      if (control.css && control.css.trim()) {
        sections.push(`/* @control ${control.name || ''} */\n${control.css.trim()}`);
      }
    });
    return sections.join('\n\n');
  }

  function writeControl(control) {
    const element = elementFor(control);

    // The name is the document's identity for a control, and it is already what the renderer puts
    // on the element and what the generated CSS selects on. Keeping the same attribute here means a
    // rule written against the stored document is the rule that applies when it runs.
    if (control.name) element.setAttribute('data-name', control.name);
    if (control.elementId) element.id = control.elementId;
    if (control.classes && control.classes.trim()) element.className = control.classes.trim();
    if (control.tip) element.title = control.tip;

    for (const [property, key] of GEOMETRY) element.style[property] = px(control[key]);

    writeColors(element, control.colors);
    writeBindings(element, control.bind);
    writeHandlers(element, control.events);

    if (KINDS[control.type] && KINDS[control.type].container) {
      for (const child of control.controls || []) element.append(writeControl(child));
    }
    return element;
  }

  // The markup a kind is. Properties HTML already has a word for are written with that word, so the
  // element is the control rather than a description of one: a placeholder is `placeholder`, a
  // read-only box is `readonly`, a drop-down's choices are its `<option>`s.
  function elementFor(control) {
    const props = control.props || {};
    switch (control.type) {
      case 'label': {
        const span = document.createElement('span');
        span.textContent = props.text ?? '';
        return span;
      }
      case 'textbox': {
        // One kind, two tags. A multi-line box is a `<textarea>` because that is what a multi-line
        // box is in HTML, and nothing has to record that it is multi-line separately.
        const element = document.createElement(props.multiline ? 'textarea' : 'input');
        if (!props.multiline) element.setAttribute('type', 'text');
        if (props.placeholder) element.setAttribute('placeholder', props.placeholder);
        if (props.readOnly) element.setAttribute('readonly', '');
        return element;
      }
      case 'textarea': {
        // The retired kind. It is still written and read so documents that used it keep loading,
        // and `data-role` is what tells it apart from a multi-line text box.
        const element = document.createElement('textarea');
        element.setAttribute('data-role', 'textarea');
        if (props.placeholder) element.setAttribute('placeholder', props.placeholder);
        return element;
      }
      case 'checkbox': {
        const label = document.createElement('label');
        label.setAttribute('data-role', 'checkbox');
        const input = document.createElement('input');
        input.setAttribute('type', 'checkbox');
        const text = document.createElement('span');
        text.textContent = props.text ?? '';
        label.append(input, text);
        return label;
      }
      case 'select': {
        const select = document.createElement('select');
        for (const option of String(props.options ?? '').split('\n').filter(Boolean)) {
          const item = document.createElement('option');
          item.textContent = option;
          select.append(item);
        }
        return select;
      }
      case 'button': {
        const button = document.createElement('button');
        button.setAttribute('type', 'button');
        button.textContent = props.text ?? '';
        return button;
      }
      case 'pager': {
        // A pager draws itself from where the component is in its rows, so there is nothing to
        // write but the two choices about how it looks. Its buttons belong to the renderer, not to
        // the document: writing them here would be storing something nobody may edit.
        const pager = document.createElement('div');
        pager.setAttribute('data-role', 'pager');
        if (props.edges) pager.setAttribute('data-edges', '');
        if (props.position) pager.setAttribute('data-position', '');
        return pager;
      }
      case 'foreign': {
        // Written back exactly as it came in. The markup goes through the serializer verbatim
        // rather than being rebuilt from a parsed tree: mixed content like `<p>Some <b>bold</b>
        // text</p>` does not survive a rebuild, and a document must not lose text it was given.
        const raw = document.createElement('gridlet-raw');
        raw.setAttribute('data-raw', props.html ?? '');
        return raw;
      }
      case 'panel': {
        const panel = document.createElement('div');
        panel.setAttribute('data-role', 'panel');
        if (props.title) {
          const title = document.createElement('div');
          title.setAttribute('data-role', 'panel-title');
          title.textContent = props.title;
          panel.append(title);
        }
        return panel;
      }
      default: {
        // A kind this build does not know is kept rather than dropped, the same way foreign markup
        // is. A newer document is refused at the envelope, but one that arrived by hand should
        // still survive being opened and saved by a build that cannot draw one of its controls.
        const unknown = document.createElement('div');
        unknown.setAttribute('data-role', control.type);
        return unknown;
      }
    }
  }

  const writeColors = (element, colors) => {
    for (const [attribute, theme, slot] of COLORS) {
      const value = colors && colors[theme] ? colors[theme][slot] : '';
      if (value) element.setAttribute(attribute, value);
    }
  };

  // A binding is the formula a property is worked out from, written with the `=` it is recognised
  // by, so what is in the file is exactly what the panel shows.
  const writeBindings = (element, bind) => {
    for (const [property, expression] of Object.entries(bind || {})) {
      if (expression && expression.trim()) {
        element.setAttribute(`data-bind-${dashed(property)}`, expression);
      }
    }
  };

  // `data-on-click`, not `onclick`. The document names what should happen; it does not carry the
  // code that happens. An `onclick` would make saving a document the same privilege as saving a
  // module, and those are deliberately not the same thing.
  const writeHandlers = (element, events) => {
    for (const [event, expression] of Object.entries(events || {})) {
      if (expression && expression.trim()) {
        element.setAttribute(`data-on-${dashed(event)}`, expression);
      }
    }
  };

  // ---- reading --------------------------------------------------------------

  function fromHtml(html) {
    const parsed = new DOMParser().parseFromString(String(html ?? ''), 'text/html');
    const root = parsed.querySelector('form[data-gridlet]');
    if (!root) throw new Error('Not a component document: no <form data-gridlet> element.');

    // The envelope is checked before anything is read out of it. A document written to a newer
    // schema is refused rather than half-understood: dropping what this build cannot read and then
    // saving would delete the parts of somebody's component it merely did not recognise.
    const schema = Number(root.getAttribute('data-gridlet'));
    if (!Number.isFinite(schema) || schema > SCHEMA) {
      throw new Error(`This component needs a newer Gridlet: it is written to schema ${schema}.`);
    }

    const doc = {
      layout: root.getAttribute('data-layout') || 'free',
      width: unpx(root.style.width) || 720,
      height: unpx(root.style.height) || 460,
      css: '',
      showScrollbars: root.hasAttribute('data-scrollbars'),
      resizable: root.hasAttribute('data-resizable'),
      isolated: root.hasAttribute('data-isolated'),
      source: null,
      elementId: root.id || '',
      classes: root.getAttribute('class') || '',
      tip: root.getAttribute('title') || '',
      colors: readColors(root),
      bind: readBindings(root),
      events: readHandlers(root),
      modules: [],
      controls: [],
    };

    for (const link of root.querySelectorAll(':scope > link[rel="gridlet-module"]')) {
      const href = link.getAttribute('href');
      if (!href) continue;
      // A module named on its own stays a plain name. Only one that names a class needs the pair,
      // so the common case reads as the file name it is.
      const className = link.getAttribute('data-class');
      doc.modules.push(className ? { module: href, class: className } : href);
    }

    const source = root.querySelector(':scope > gridlet-source');
    if (source && source.getAttribute('href')) {
      const parameters = {};
      for (const parameter of source.querySelectorAll(':scope > param[name]')) {
        parameters[parameter.getAttribute('name')] = parameter.getAttribute('value') || '';
      }
      doc.source = { route: source.getAttribute('href'), parameters };
    }

    const sheet = root.querySelector(':scope > style');
    const sections = readStyleSheet(sheet ? sheet.textContent : '');
    doc.css = sections.component;

    for (const child of root.children) {
      const control = readControl(child, sections.byControl);
      if (control) doc.controls.push(control);
    }
    return doc;
  }

  // Splits the stylesheet back into the component's own CSS and each control's, on the markers the
  // writer left. Anything before the first marker is the component's, which is also what a
  // stylesheet written by hand with no markers at all comes back as — so hand-editing degrades to
  // "it is all the component's CSS" rather than to losing it.
  function readStyleSheet(text) {
    const source = String(text || '');
    const marker = /\/\*\s*@control\s+([^*]*?)\s*\*\//g;
    const byControl = new Map();

    let match = marker.exec(source);
    if (!match) return { component: source.trim(), byControl };

    const component = source.slice(0, match.index);
    while (match) {
      const name = match[1];
      const start = match.index + match[0].length;
      const next = marker.exec(source);
      byControl.set(name, source.slice(start, next ? next.index : source.length).trim());
      match = next;
    }
    return { component: component.trim(), byControl };
  }

  function readControl(element, cssByControl) {
    const type = kindOf(element);
    if (!type) return null;

    const name = element.getAttribute('data-name') || '';
    const control = {
      // The designer's own handle on a control, regenerated on every read. It is not written to the
      // file: the document identifies a control by the name its author gave it, and a random id in
      // a diff would be a change to something nobody changed.
      id: 'c' + Math.random().toString(36).slice(2, 10),
      type,
      name,
      x: unpx(element.style.left),
      y: unpx(element.style.top),
      w: unpx(element.style.width),
      h: unpx(element.style.height),
      props: readProps(element, type),
      colors: readColors(element),
      bind: readBindings(element),
      events: readHandlers(element),
      css: type === 'foreign' ? '' : (cssByControl.get(name) || ''),
      elementId: element.id || '',
      classes: element.getAttribute('class') || '',
      tip: element.getAttribute('title') || '',
    };

    if (KINDS[type] && KINDS[type].container) {
      control.controls = [];
      for (const child of element.children) {
        if (child.getAttribute('data-role') === 'panel-title') continue;
        const nested = readControl(child, cssByControl);
        if (nested) control.controls.push(nested);
      }
    }
    return control;
  }

  // Which kind an element is. The tag decides it wherever a tag is enough, and `data-role` decides
  // it where one tag serves two kinds — a `<textarea>` is a multi-line text box unless it says it
  // is the retired kind, and a `<div>` is nothing at all unless it says what it is.
  function kindOf(element) {
    const tag = element.tagName.toLowerCase();
    const role = element.getAttribute('data-role');
    // The document's own furniture: not controls, and never mistaken for one.
    if (tag === 'link' || tag === 'style' || tag === 'gridlet-source' || tag === 'param') return null;
    if (role && KINDS[role]) return role;
    switch (tag) {
      case 'textarea': return 'textbox';
      case 'input': return 'textbox';
      case 'span': return 'label';
      case 'select': return 'select';
      case 'button': return 'button';
      // Everything else is somebody's own markup. It is kept and drawn, not discarded.
      default: return 'foreign';
    }
  }

  function readProps(element, type) {
    switch (type) {
      case 'label':
        return { text: element.textContent ?? '' };
      case 'textbox':
        return {
          placeholder: element.getAttribute('placeholder') || '',
          multiline: element.tagName.toLowerCase() === 'textarea',
          readOnly: element.hasAttribute('readonly'),
        };
      case 'textarea':
        return { placeholder: element.getAttribute('placeholder') || '' };
      case 'checkbox': {
        const text = element.querySelector('span');
        return { text: text ? text.textContent : '' };
      }
      case 'select':
        return {
          options: [...element.querySelectorAll('option')]
            .map((option) => option.textContent).join('\n'),
        };
      case 'button':
        return { text: element.textContent ?? '' };
      case 'pager':
        return {
          edges: element.hasAttribute('data-edges'),
          position: element.hasAttribute('data-position'),
        };
      case 'panel': {
        const title = element.querySelector('[data-role="panel-title"]');
        return { title: title ? title.textContent : '' };
      }
      case 'foreign':
        return { html: element.outerHTML };
      default:
        return {};
    }
  }

  function readColors(element) {
    const colors = { light: { text: '', background: '' }, dark: { text: '', background: '' } };
    for (const [attribute, theme, slot] of COLORS) {
      colors[theme][slot] = element.getAttribute(attribute) || '';
    }
    return colors;
  }

  const readPrefixed = (element, prefix) => {
    const values = {};
    for (const attribute of element.attributes) {
      if (attribute.name.startsWith(prefix)) {
        values[camelled(attribute.name.slice(prefix.length))] = attribute.value;
      }
    }
    return values;
  };

  const readBindings = (element) => readPrefixed(element, 'data-bind-');
  const readHandlers = (element) => readPrefixed(element, 'data-on-');

  // ---- shared ---------------------------------------------------------------

  // Property names are camelCase in the document model and an attribute cannot be, so the two
  // spellings convert both ways rather than one name being kept in two forms.
  const dashed = (name) => String(name).replace(/[A-Z]/g, (c) => '-' + c.toLowerCase());
  const camelled = (name) => String(name).replace(/-([a-z])/g, (_, c) => c.toUpperCase());

  function walk(controls, visit) {
    for (const control of controls) {
      visit(control);
      if (control.controls) walk(control.controls, visit);
    }
  }

  // Written out indented, one element per line, because the file is the artifact: it is read in
  // review and diffed there. The browser's own serializer puts a document on one line, which would
  // make every change look like a change to everything.
  function serialize(root) {
    const lines = [];
    const write = (element, depth) => {
      const pad = '  '.repeat(depth);
      const attributes = [...element.attributes]
        .map((a) => (a.value === '' ? ` ${a.name}` : ` ${a.name}="${escapeAttribute(a.value)}"`))
        .join('');
      const tag = element.tagName.toLowerCase();

      // Foreign markup is printed exactly as it arrived. Only the first line is indented to sit
      // with its siblings: re-indenting the rest would change whitespace that is content inside a
      // `<pre>`, and would make the next read differ from this write.
      if (tag === 'gridlet-raw') {
        const raw = (element.getAttribute('data-raw') || '').split('\n');
        lines.push(pad + raw[0]);
        for (const line of raw.slice(1)) lines.push(line);
        return;
      }

      if (VOID_TAGS.has(tag)) {
        lines.push(`${pad}<${tag}${attributes}>`);
        return;
      }

      const children = [...element.children];
      // An element whose whole content is text stays on one line. Breaking `<button>Save</button>`
      // over three lines would also change what it contains, because the whitespace would be text.
      if (!children.length) {
        lines.push(`${pad}<${tag}${attributes}>${escapeText(element.textContent ?? '')}</${tag}>`);
        return;
      }

      lines.push(`${pad}<${tag}${attributes}>`);
      for (const child of children) write(child, depth + 1);
      lines.push(`${pad}</${tag}>`);
    };
    write(root, 0);
    return lines.join('\n') + '\n';
  }

  const VOID_TAGS = new Set(['link', 'input', 'br', 'hr', 'img', 'meta', 'param']);

  const escapeAttribute = (value) => String(value)
    .replace(/&/g, '&amp;').replace(/"/g, '&quot;')
    .replace(/</g, '&lt;').replace(/>/g, '&gt;');

  const escapeText = (value) => String(value)
    .replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;');

  window.GridletComponentFormat = { SCHEMA, toHtml, fromHtml };
})();
