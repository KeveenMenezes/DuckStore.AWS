// Warehouse input rules (ADR-0052 §4). The AppSync resolvers under graphql/resolvers/inventory are
// the authority; this copy serves local.ts, and the Blazor WarehouseForm mirrors it too. A change
// here MUST land in all three, and warehouse-validation.test.ts pins the cases the resolvers are
// checked against with `aws appsync evaluate-code`.

export const WAREHOUSE_CODE_PATTERN = /^[A-Z0-9][A-Z0-9-]{1,19}$/
export const MAX_WAREHOUSE_PRIORITY = 999

export const BRAZILIAN_STATES = [
  'AC', 'AL', 'AP', 'AM', 'BA', 'CE', 'DF', 'ES', 'GO', 'MA', 'MT', 'MS', 'MG', 'PA',
  'PB', 'PR', 'PE', 'PI', 'RJ', 'RN', 'RS', 'RO', 'RR', 'SC', 'SP', 'SE', 'TO',
]

export type WarehouseAddressInput = {
  street: string
  number: string
  complement?: string | null
  district: string
  city: string
  state: string
  zipCode: string
}

export type WarehouseFieldsInput = {
  name: string
  address: WarehouseAddressInput
  priority: number
}

export type WarehouseFields = {
  name: string
  street: string
  number: string
  complement: string | null
  district: string
  city: string
  state: string
  zipCode: string
  priority: number
}

export type WarehouseValidationError = { field: string; message: string }

export type Validated<T> = { value: T; error?: undefined } | { value?: undefined; error: WarehouseValidationError }

// [field, min, max] for the free-text fields, in the order they are reported.
const TEXT_RULES: Array<[path: string, min: number, max: number]> = [
  ['name', 1, 80],
  ['address.street', 1, 120],
  ['address.number', 1, 10],
  ['address.complement', 0, 60],
  ['address.district', 1, 60],
  ['address.city', 1, 60],
]

export function normalizeWarehouseCode(code: string): Validated<string> {
  const normalized = (code ?? '').trim().toUpperCase()
  if (!WAREHOUSE_CODE_PATTERN.test(normalized)) {
    return {
      error: {
        field: 'code',
        message: 'code must be 2-20 characters: letters, digits or hyphens, starting with a letter or digit',
      },
    }
  }
  return { value: normalized }
}

// Normalizes (trim, uppercase UF, CEP without hyphen) and validates the fields shared by create and
// update; the first failing field is reported.
export function normalizeWarehouseFields(input: WarehouseFieldsInput): Validated<WarehouseFields> {
  const address = input.address ?? ({} as WarehouseAddressInput)
  const text: Record<string, string> = {
    name: (input.name ?? '').trim(),
    'address.street': (address.street ?? '').trim(),
    'address.number': (address.number ?? '').trim(),
    'address.complement': (address.complement ?? '').trim(),
    'address.district': (address.district ?? '').trim(),
    'address.city': (address.city ?? '').trim(),
  }

  for (const [path, min, max] of TEXT_RULES) {
    const length = text[path].length
    if (length < min || length > max) {
      const label = path.replace('address.', '')
      const message = min === 0 ? `${label} must be at most ${max} characters` : `${label} must be ${min}-${max} characters`
      return { error: { field: path, message } }
    }
  }

  const state = (address.state ?? '').trim().toUpperCase()
  if (!BRAZILIAN_STATES.includes(state)) {
    return { error: { field: 'address.state', message: 'state must be a Brazilian UF, e.g. SP' } }
  }

  const zipCode = (address.zipCode ?? '').trim().replace(/-/g, '')
  if (!/^\d{8}$/.test(zipCode)) {
    return { error: { field: 'address.zipCode', message: 'zipCode must have 8 digits (00000-000)' } }
  }

  const priority = input.priority
  if (!Number.isInteger(priority) || priority < 1 || priority > MAX_WAREHOUSE_PRIORITY) {
    return { error: { field: 'priority', message: `priority must be an integer between 1 and ${MAX_WAREHOUSE_PRIORITY}` } }
  }

  return {
    value: {
      name: text.name,
      street: text['address.street'],
      number: text['address.number'],
      complement: text['address.complement'] || null,
      district: text['address.district'],
      city: text['address.city'],
      state,
      zipCode,
      priority,
    },
  }
}
