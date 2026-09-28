// Optional cross-check against the official Portal.js downloaded for review.
const fs = require('fs');
const vm = require('vm');
const source = fs.readFileSync(process.argv[2], 'utf8');
const start = source.indexOf('        function encode(str, key)');
const end = source.indexOf("        return '{SRBX1}'", start);
if (start < 0 || end < 0) throw Error('Official function not found');
const context = {};
vm.createContext(context);
vm.runInContext(source.slice(start, end), context, {timeout: 1000});
const input = JSON.stringify({username:'test',password:'password',ip:'10.0.0.1',acid:'60',enc_ver:'srun_bx1'});
const result = context.encode(input, '0123456789abcdef0123456789abcdef');
const normal = 'ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789+/';
const custom = 'LVoJPiCN2R8G90yg+hmFHuacZ1OWMnrsSTXkYpUq/3dlbfKwv6xztjI7DeBE45QA';
console.log('{SRBX1}' + Buffer.from(result,'latin1').toString('base64').split('').map(c=>c==='='?c:custom[normal.indexOf(c)]).join(''));
