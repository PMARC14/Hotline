from pathlib import Path
import json,re
p=Path('schemas/toolbar.v3.schema.json')
s=p.read_text(encoding='utf-8')
ids=['stop','spacer']+[k for k in json.loads(Path('src/Hotline.Core/Defaults/toolbar.json').read_text())['shortcuts'] if k!='stop']
reserved='|'.join(''.join('['+c.upper()+c.lower()+']' if c.isalpha() else c for c in key) for key in ids)
pattern='^(?!(?:'+reserved+')$)[A-Za-z0-9-]{1,32}$'
s=re.sub(r'"propertyNames": \{ "pattern": ".*?" \}',lambda m:'"propertyNames": { "pattern": '+json.dumps(pattern)+' }',s)
s=s.replace("Custom button definitions keyed by id; 'stop' and 'spacer' are refused (unit: button map).",'Custom button definitions keyed by 1-32 ASCII letters (either case), digits or hyphens; ids match without case, first definition wins, built-ins and stop/spacer are reserved (unit: button map).')
old=json.loads(s)['$defs']['customButton']['properties']['iconFile']['pattern']
s=s.replace(json.dumps(old),json.dumps('^[A-Za-z0-9-]{1,32}\\.[Pp][Nn][Gg]$'))
s=s.replace("Custom PNG filename in icons\\\\ matching '<id>.png' (unit: filename).","Custom PNG filename in icons\\\\ matching '<id>.png' without regard to id or extension case (unit: filename).")
p.write_text(s,encoding='utf-8')
print(pattern)
print(json.loads(s)['$defs']['customButton']['properties']['iconFile']['pattern'])
