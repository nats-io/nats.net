const fs = require('fs')
const path = require('path')

const source = path.join(__dirname, 'schema_source', 'jetstream', 'api', 'v1')
const dest = path.join(__dirname, 'schema', 'jetstream.api.v1.json')
const defs = JSON.parse(fs.readFileSync(path.join(source, 'definitions.json'), 'utf-8'))

for (const f of fs.readdirSync(source)) {
  if (f == 'definitions.json') {
    continue
  }
  const typeName = f.split('.json')[0]
  console.log(`merging ${typeName}`)
  const schema = JSON.parse(fs.readFileSync(path.join(source, f), 'utf-8'))
  delete schema['$id']
  delete schema['$schema']
  defs.definitions[typeName] = schema
}

let contents = JSON.stringify(defs, null, 2).replaceAll()
console.log(`writing ${dest}`)
fs.writeFileSync(dest, JSON.stringify(defs, null, 2).replaceAll("definitions.json#/definitions", "#/definitions"), 'utf-8')
