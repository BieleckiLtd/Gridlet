// my.js
//
// Behaviour for a Gridlet component. The component is handed to you; the rest is ordinary JavaScript —
// import what you like, export what you like, keep what is yours private.
const sizeOf = (component) => {
  const box = component.element.getBoundingClientRect();
  return Math.round(box.width) + ' x ' + Math.round(box.height);
};

// Called by a handler formula, so `this` is the component.
export function showSize() {
  this.field('label3').value = 'handler: ' + sizeOf(this);
}

export default class My {
  #component;

  constructor(component) {
    this.#component = component;
  }

  // Called once the component is running and its rows have loaded.
  connected() {
    this.#component.on('row', (row) => this.#rowChanged(row));
  }

  #rowChanged(row) {
    // this.#component.field('total').value = row.Price * row.Quantity;
  }
  width() {
    return this.#component.element.getBoundingClientRect().width;
  }
}
