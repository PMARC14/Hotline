import { test } from 'node:test';
import assert from 'node:assert/strict';
import { initialState, reduce } from '../../src/Hotline.App/Web/chat-core.js';

const run = (...msgs) => msgs.reduce(reduce, initialState());

test('user + streamed assistant message', () => {
  const s = run(
    { type: 'user', id: 'u1', text: 'hi', attachments: [] },
    { type: 'assistantStart', id: 'a1', backend: 'Gemini (Antigravity)' },
    { type: 'delta', id: 'a1', text: 'Hel', replace: false },
    { type: 'delta', id: 'a1', text: 'lo', replace: false },
    { type: 'done', id: 'a1' });
  assert.equal(s.messages.length, 2);
  assert.deepEqual(s.messages[1], { id: 'a1', role: 'assistant', text: 'Hello', status: 'done', backend: 'Gemini (Antigravity)' });
  assert.equal(s.busy, false);
});

test('replace delta overwrites text', () => {
  const s = run({ type: 'assistantStart', id: 'a', backend: 'x' },
    { type: 'delta', id: 'a', text: 'draft', replace: false },
    { type: 'delta', id: 'a', text: 'final', replace: true });
  assert.equal(s.messages[0].text, 'final');
  assert.equal(s.busy, true);
});

test('error marks message and clears busy', () => {
  const s = run({ type: 'assistantStart', id: 'a', backend: 'x' },
    { type: 'error', id: 'a', code: 'NotInstalled', message: 'agy missing' });
  assert.equal(s.messages[0].status, 'error');
  assert.deepEqual(s.messages[0].error, { code: 'NotInstalled', message: 'agy missing' });
  assert.equal(s.busy, false);
});

test('error for an answer that never started still shows', () => {
  const s = run({ type: 'error', id: 'a', code: 'NotConfigured', message: 'nope' });
  assert.equal(s.messages[0].role, 'assistant');
  assert.equal(s.messages[0].status, 'error');
});

test('attachments add/remove/clear', () => {
  let s = run({ type: 'attachmentAdded', id: '1', name: 'a.png', kind: 'Image', thumb: 'data:x' },
    { type: 'attachmentAdded', id: '2', name: 'b.md', kind: 'Text', thumb: null });
  assert.deepEqual(s.attachments.map(a => a.id), ['1', '2']);
  s = reduce(s, { type: 'attachmentRemoved', id: '1' });
  assert.deepEqual(s.attachments.map(a => a.id), ['2']);
  s = reduce(s, { type: 'attachmentsCleared' });
  assert.equal(s.attachments.length, 0);
});

test('reset clears messages and attachments but keeps backends', () => {
  let s = run({ type: 'backends', items: [{ id: 'agy', name: 'G', available: true }], selected: 'agy' },
    { type: 'user', id: 'u', text: 'x', attachments: [] },
    { type: 'attachmentAdded', id: '1', name: 'a.png', kind: 'Image', thumb: null });
  s = reduce(s, { type: 'reset' });
  assert.equal(s.messages.length, 0);
  assert.equal(s.attachments.length, 0);
  assert.equal(s.selectedBackend, 'agy');
});

test('unknown messages leave state unchanged', () => {
  const s0 = initialState();
  assert.equal(reduce(s0, { type: 'mystery' }), s0);
});
