// form-actions.js
//
// Behaviour shared by the forms in this workspace. Add, Update and Delete are declared actions, so
// the runtime sends them on its own and this file never touches the database. What is left is what
// an action cannot say by itself: what an empty record looks like, and what should happen on the
// screen once a write has gone through.
//
// One file holds them all because they behave the same way and differ only in those two answers.
// Each component names the class it runs:
//
//   <gridlet-code src="form-actions.js" run="Kitchen"></gridlet-code>

class FormActions {
  #component;
  #observer;
  #lastStatus = '';

  constructor(component) {
    this.#component = component;
  }

  // Handler formulas call a module's methods, and those methods still need the component.
  get component() {
    return this.#component;
  }

  // What the New button puts in each field. A field left out of this keeps what it is showing.
  get emptyRecord() {
    return {};
  }

  // The field New leaves the caret in.
  get firstField() {
    return '';
  }

  connected() {
    // A form without these buttons simply has nothing to wire; `field` is safe about that.
    this.#component.field('newButton').on('click', () => this.#clear());
    this.#component.field('refreshButton').on('click', () => { void this.reload(); });
    this.#watchActions();
  }

  disconnected() {
    this.#observer?.disconnect();
    this.#observer = null;
  }

  // A write is about to be sent. Anything on screen that describes the last one is now stale.
  writeStarted() {}

  // What the screen should do once a write has gone through. A form over a list is out of date
  // until it reads the records back, which is why that is the default.
  writeSucceeded() {
    void this.reload();
  }

  // The status line already says what went wrong; this is for anything else the screen was showing.
  writeFailed() {}

  async reload() {
    await this.#component.reload();
  }

  #clear() {
    for (const [name, value] of Object.entries(this.emptyRecord)) {
      this.#component.field(name).value = value;
    }
    if (this.firstField) this.#component.field(this.firstField).focus();
  }

  // The runtime reports every action through one status line, so the class that line carries is
  // how a form learns that a write started, went through, or did not. The class is compared with
  // the one seen last, so whatever the form does next cannot look like another change.
  #watchActions() {
    const root = this.#component.element;
    this.#observer = new MutationObserver(() => {
      const status = root.querySelector(':scope > .gridlet-action-status');
      const current = status ? status.className : '';
      if (current === this.#lastStatus) return;
      this.#lastStatus = current;
      if (!status) return;
      if (status.classList.contains('pending')) this.writeStarted();
      else if (status.classList.contains('success')) this.writeSucceeded();
      else if (status.classList.contains('error')) this.writeFailed();
    });
    this.#observer.observe(root, {
      childList: true,
      subtree: true,
      attributes: true,
      attributeFilter: ['class'],
    });
  }
}

/** The back-office screen: a list of customers, edited in place. */
export class CustomerForm extends FormActions {
  // The key is left empty because the database allocates it.
  get emptyRecord() {
    return { customerId: '', firstName: '', lastName: '', email: '', points: '0', marketing: false };
  }

  get firstField() {
    return 'firstName';
  }
}

/** The page a customer orders from. */
export class OrderForm extends FormActions {
  connected() {
    super.connected();
    // What the form starts on. This belongs here rather than in the markup: a `selected` option and
    // an input's `value` are not properties the designer keeps, so either would be dropped the
    // first time somebody saved this component from the designer.
    this.component.field('size').value = 'Medium';
    this.component.field('quantity').value = '1';
    // Size and quantity call showTotal from their own handler formulas. Choosing another pizza is
    // the third thing that changes the price, and reading the menu again is the fourth.
    this.component.on('row', () => this.showTotal());
    this.component.on('load', () => this.showTotal());
    this.showTotal();
  }

  // The price of the size the customer picked. The menu row carries a column per size, named after
  // the size, so this form knows no prices - it asks the row for the one that applies. The server
  // prices the order again when it is sent; this is only what the customer is shown while deciding.
  showTotal() {
    const component = this.component;
    const row = component.row;
    const size = component.field('size').value;
    const quantity = Number(component.field('quantity').value);
    const price = row && Object.hasOwn(row, size) ? Number(row[size]) : NaN;
    component.field('total').value =
      Number.isFinite(price) && Number.isFinite(quantity) && quantity > 0
        ? `£${(price * quantity).toFixed(2)}`
        : '';
  }

  // The menu does not change because somebody ordered from it, and re-reading it would throw away
  // the pizza the customer had chosen. Say thank you instead, and leave the page where it is.
  writeSucceeded() {
    this.component.field('confirmation').value =
      'Thank you. Your order is with the kitchen.';
  }

  // A thank you for the last order must not still be on screen while this one is being sent, or
  // worse, while the status line says this one did not go through.
  writeStarted() {
    this.component.field('confirmation').value = '';
  }
}

/** The screen the kitchen works from. */
export class Kitchen extends FormActions {
  // Advancing a ticket changes what is in the queue and what the button on it should say next, so
  // the queue is read again. An order that is finished drops out of it.
  writeSucceeded() {
    void this.reload();
  }
}
