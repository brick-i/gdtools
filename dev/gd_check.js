// gd_check.js — независимый декодер GD-сейва (Node), для перекрёстной проверки
// того, что пишет gd_currency.exe. Специально без общих с ним кусков кода.
//
// usage: node gd_check.js <file.dat>
const fs = require('fs');
const zlib = require('zlib');

const file = process.argv[2];
if (!file) {
  console.error('usage: node gd_check.js <file.dat>');
  process.exit(1);
}

const KEY = 0x0b;
const blob = fs.readFileSync(file);

// XOR 0x0B -> base64url -> base64 -> gzip -> plist
const out = Buffer.alloc(blob.length);
for (let i = 0; i < blob.length; i++) out[i] = blob[i] ^ KEY;

const b64 = out.toString('ascii').trim().replace(/-/g, '+').replace(/_/g, '/');
const xml = zlib.gunzipSync(Buffer.from(b64, 'base64')).toString('utf8');

console.log(`файл : ${file}`);
console.log(`xml  : ${xml.length} симв., корень ${xml.slice(0, 60).replace(/\n/g, ' ')}...`);

const m = /<k>GS_value<\/k><d>(.*?)<\/d>/.exec(xml);
if (m) {
  const rows = [...m[1].matchAll(/<k>([^<]+)<\/k><(?:i|s|r)>([^<]*)<\/(?:i|s|r)>/g)];
  console.log(`GS_value: ${rows.length} статов`);
  console.log(rows.map((r) => `${r[1]}=${r[2]}`).join('  '));
} else {
  console.log('GS_value: не найден');
}
console.log('XML валиден как plist:', /^<\?xml/.test(xml) && /<\/plist>\s*$/.test(xml));
