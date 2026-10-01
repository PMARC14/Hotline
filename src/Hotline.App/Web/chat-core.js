// Pure chat state reducer (tested with `node --test tests/web`). No DOM access here.
export function initialState() {
  return { messages: [], attachments: [], backends: [], selectedBackend: null, busy: false };
}

function updateMessage(state, id, fn, createIfMissing) {
  let found = false;
  const messages = state.messages.map(m => (m.id === id ? ((found = true), fn(m)) : m));
  if (!found && createIfMissing) messages.push(fn(createIfMissing));
  return messages;
}

export function reduce(state, msg) {
  switch (msg.type) {
    case 'user':
      return { ...state, messages: [...state.messages, { id: msg.id, role: 'user', text: msg.text, attachments: msg.attachments ?? [] }] };
    case 'assistantStart':
      return { ...state, busy: true,
        messages: [...state.messages, { id: msg.id, role: 'assistant', text: '', status: 'streaming', backend: msg.backend }] };
    case 'delta':
      return { ...state, messages: updateMessage(state, msg.id, m => ({ ...m, text: msg.replace ? msg.text : m.text + msg.text })) };
    case 'done':
    case 'cancelled':
      return { ...state, busy: false, messages: updateMessage(state, msg.id, m => ({ ...m, status: msg.type })) };
    case 'error': {
      const placeholder = { id: msg.id, role: 'assistant', text: '', backend: null };
      const { status: _ignored, ...rest } = placeholder;
      return { ...state, busy: false,
        messages: updateMessage(state, msg.id, m => ({ ...m, status: 'error', error: { code: msg.code, message: msg.message } }), rest) };
    }
    case 'reset':
      return { ...state, messages: [], attachments: [], busy: false };
    case 'backends':
      return { ...state, backends: msg.items, selectedBackend: msg.selected };
    case 'attachmentAdded':
      return { ...state, attachments: [...state.attachments, { id: msg.id, name: msg.name, kind: msg.kind, thumb: msg.thumb ?? null }] };
    case 'attachmentRemoved':
      return { ...state, attachments: state.attachments.filter(a => a.id !== msg.id) };
    case 'attachmentsCleared':
      return { ...state, attachments: [] };
    default:
      return state;
  }
}

/** Height the popup should have: the composer alone, or composer + the messages' natural height. */
export function desiredHeight({ messageHeights, gap, listPadding, composer, chrome }) {
  if (messageHeights.length === 0) return Math.ceil(composer + chrome);
  const content = messageHeights.reduce((a, b) => a + b, 0) + gap * (messageHeights.length - 1);
  return Math.ceil(content + listPadding + composer + chrome);
}

const MAX_READ_BYTES = 20 * 1024 * 1024; // matches the host's largest limit (images); the host re-checks precisely

/** Rejects files that are too big to even read/transfer; returns a message, or null to proceed. */
export function preReadRejection(file) {
  return file.size > MAX_READ_BYTES ? `${file.name} is too large (max 20 MB).` : null;
}
