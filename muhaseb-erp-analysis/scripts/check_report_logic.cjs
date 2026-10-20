/* Exercises actual embedded report JavaScript with a minimal DOM.
   This checks filtering/navigation logic, not browser layout. */
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
const assert = require('node:assert/strict');
const root = path.resolve(__dirname, '..');
const html = fs.readFileSync(path.join(root, 'index.html'), 'utf8');
const screens = JSON.parse(fs.readFileSync(path.join(root, 'data/screens.json'), 'utf8'));
function element() {
  const classes = new Set();
  return {value:'', style:{}, textContent:'', listeners:{}, open:false,
    classList:{contains:c=>classes.has(c), toggle:(c,b)=>b?classes.add(c):classes.delete(c)},
    addEventListener(k,f){this.listeners[k]=f}, scrollIntoView(){this.scrolled=true}};
}
const cards = screens.map(s=>{
  const e=element();
  e.id=s.id;e.tagName='DETAILS';e.classList.toggle('screen',true);
  e.dataset={module:s.module,scope:s.scope,search:[s.id,s.name,s.purpose,...s.fields.map(f=>f.name)].join(' ')};
  return e;
});
const controls=Object.fromEntries(['search','module','scope','count','empty','expand','collapse'].map(x=>[x,element()]));
const context={
  document:{querySelector:s=>controls[s.slice(1)],querySelectorAll:()=>cards,getElementById:id=>cards.find(c=>c.id===id)},
  location:{hash:''}, window:{addEventListener(){}}, requestAnimationFrame:f=>f(),
};
vm.createContext(context);
const code=html.match(/<script>([\s\S]*?)<\/script>/)[1];
vm.runInContext(code,context);
const visible=()=>cards.filter(c=>!c.classList.contains('hidden'));
assert.equal(visible().length,150);
controls.search.value='SAL06';controls.search.listeners.input();
assert.deepEqual(visible().map(c=>c.id),['SAL06']);
controls.search.value='';controls.module.value='GL';controls.module.listeners.input();
assert.equal(visible().length,screens.filter(s=>s.module==='GL').length);
controls.expand.onclick();assert(visible().every(c=>c.open));
controls.collapse.onclick();assert(cards.every(c=>!c.open));
controls.search.value='غير موجود تماما xyz';controls.search.listeners.input();
assert.equal(visible().length,0);assert.equal(controls.empty.style.display,'block');
context.location.hash='#SAL06';vm.runInContext('openHash()',context);
assert.equal(visible().length,150);assert(cards.find(c=>c.id==='SAL06').open);
controls.search.value='اصول';controls.search.listeners.input();
assert(visible().length>0);
const result={passed:true,checks:10,scope:'منطق البحث والترشيح والتنقل من الكود الفعلي في بيئة DOM محدودة؛ ليس اختبار عرض متصفح'};
fs.writeFileSync(path.join(root,'data/report-logic-check.json'),JSON.stringify(result,null,2));
console.log(JSON.stringify(result));
