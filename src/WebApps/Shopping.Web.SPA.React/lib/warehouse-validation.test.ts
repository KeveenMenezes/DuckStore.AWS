import { test } from 'node:test'
import assert from 'node:assert/strict'
import { normalizeWarehouseCode, normalizeWarehouseFields, type WarehouseFieldsInput } from './warehouse-validation.ts'

// The same cases are run against the AppSync resolvers with `aws appsync evaluate-code`, so the
// resolver and this copy can't drift apart unnoticed (ADR-0052 §4).

const valid: WarehouseFieldsInput = {
  name: 'Polo Alpha',
  address: {
    street: 'Avenida Paulista',
    number: '1000',
    complement: null,
    district: 'Bela Vista',
    city: 'São Paulo',
    state: 'SP',
    zipCode: '01310100',
  },
  priority: 1,
}

const withAddress = (patch: Partial<WarehouseFieldsInput['address']>): WarehouseFieldsInput => ({
  ...valid,
  address: { ...valid.address, ...patch },
})

test('code: accepts 2 and 20 characters and normalizes to uppercase', () => {
  assert.equal(normalizeWarehouseCode('a1').value, 'A1')
  assert.equal(normalizeWarehouseCode('  alpha ').value, 'ALPHA')
  assert.equal(normalizeWarehouseCode('SP-01').value, 'SP-01')
  assert.equal(normalizeWarehouseCode('A'.repeat(20)).value, 'A'.repeat(20))
})

for (const code of ['A', 'A'.repeat(21), '-ALPHA', 'AL PHA', 'ALPHA_1', '', 'ÁLPHA']) {
  test(`code: rejects ${JSON.stringify(code)}`, () => {
    assert.equal(normalizeWarehouseCode(code).error?.field, 'code')
  })
}

test('fields: a valid input is normalized', () => {
  const result = normalizeWarehouseFields({
    ...withAddress({ state: ' sp ', zipCode: '01310-100', complement: '  ' }),
    name: '  Polo Alpha  ',
  })
  assert.deepEqual(result.value, {
    name: 'Polo Alpha',
    street: 'Avenida Paulista',
    number: '1000',
    complement: null,
    district: 'Bela Vista',
    city: 'São Paulo',
    state: 'SP',
    zipCode: '01310100',
    priority: 1,
  })
})

const rejections: Array<[label: string, input: WarehouseFieldsInput, field: string]> = [
  ['empty name', { ...valid, name: '   ' }, 'name'],
  ['name over 80', { ...valid, name: 'x'.repeat(81) }, 'name'],
  ['empty street', withAddress({ street: '' }), 'address.street'],
  ['street over 120', withAddress({ street: 'x'.repeat(121) }), 'address.street'],
  ['number over 10', withAddress({ number: '12345678901' }), 'address.number'],
  ['complement over 60', withAddress({ complement: 'x'.repeat(61) }), 'address.complement'],
  ['empty district', withAddress({ district: '' }), 'address.district'],
  ['empty city', withAddress({ city: '' }), 'address.city'],
  ['unknown UF', withAddress({ state: 'XX' }), 'address.state'],
  ['zipCode with 7 digits', withAddress({ zipCode: '0131010' }), 'address.zipCode'],
  ['zipCode with letters', withAddress({ zipCode: '0131O100' }), 'address.zipCode'],
  ['priority 0', { ...valid, priority: 0 }, 'priority'],
  ['priority 1000', { ...valid, priority: 1000 }, 'priority'],
  ['priority 1.5', { ...valid, priority: 1.5 }, 'priority'],
]

for (const [label, input, field] of rejections) {
  test(`fields: rejects ${label}`, () => {
    assert.equal(normalizeWarehouseFields(input).error?.field, field)
  })
}

test('fields: accepts the boundaries', () => {
  assert.ok(normalizeWarehouseFields({ ...valid, priority: 999 }).value)
  assert.ok(normalizeWarehouseFields({ ...valid, name: 'x'.repeat(80) }).value)
  assert.ok(normalizeWarehouseFields(withAddress({ complement: 'x'.repeat(60) })).value)
})
