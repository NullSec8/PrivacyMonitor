const test = require('node:test');
const assert = require('node:assert');

// Signal to the server not to start the HTTP listener when required from tests.
process.env.PM_TEST = '1';

// Require the server module to access helpers (without starting the listener).
const { sanitizeLogBody } = require('../server');

test('sanitizeLogBody returns empty object for non-object input', () => {
  assert.deepStrictEqual(sanitizeLogBody(null, ['a', 'b']), {});
  assert.deepStrictEqual(sanitizeLogBody(undefined, ['a']), {});
  assert.deepStrictEqual(sanitizeLogBody('not-an-object', ['a']), {});
});

test('sanitizeLogBody only keeps allowed keys and stringifies values', () => {
  const input = {
    version: '1.2.3',
    platform: 'win64',
    client: 'PrivacyMonitor',
    clientId: 12345,
    extra: 'should-be-stripped',
  };
  const allowed = ['version', 'platform', 'client', 'clientId'];
  const out = sanitizeLogBody(input, allowed);

  // Only allowed keys present
  assert.deepStrictEqual(Object.keys(out).sort(), allowed.sort());

  // Values are strings
  assert.strictEqual(out.version, '1.2.3');
  assert.strictEqual(out.platform, 'win64');
  assert.strictEqual(out.client, 'PrivacyMonitor');
  assert.strictEqual(typeof out.clientId, 'string');
  assert.strictEqual(out.clientId, '12345');
});

test('sanitizeLogBody truncates overly long values to 500 characters', () => {
  const long = 'x'.repeat(800);
  const out = sanitizeLogBody({ version: long }, ['version']);
  assert.strictEqual(out.version.length, 500);
});

