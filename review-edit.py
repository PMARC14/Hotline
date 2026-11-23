from pathlib import Path
import re
def edit(path,f):
 p=Path(path);p.write_text(f(p.read_text(encoding='utf-8-sig')),encoding='utf-8')
for p in Path('src/Hotline.Core/Text/Localization').glob('Strings*.resx'):
 edit(p,lambda s:re.sub(r'(<data name="Bar_ChangedOnDisk"[^>]*>.*?</data>)',lambda m:m[0]+'\n  <data name="Bar_DuplicateButton" xml:space="preserve">\n    <value>Duplicate custom button id "{0}" (case-insensitive); the first definition is used.</value>\n  </data>',s,count=1,flags=re.S))
# Keep the test cases, remove vacuous assertions and state their current v3 behavior.
def tests(s):
 a=s.index('    [Theory]\n    [InlineData("\\"size\\":'); b=s.index('    [Theory]',a+15)
 part=s[a:b]
 part=re.sub(r'(\[InlineData\(".*?"), (?:2|0.5|3|4)\)\]',r'\1)]',part)
 part=part.replace('string alias, double cells','string alias').replace('        Assert.True(cells > 0); // All former aliases are deliberately exercised.\n','')
 part=part.replace(' // an item that had both: cells won','').replace(' // written as width from now on','')
 s=s[:a]+part+s[b:]
 return s.replace('Width_wins_over_its_old_names_with_one_warning','Numeric_width_applies_and_old_names_are_ignored_without_warning').replace('An_invalid_width_falls_back_to_a_valid_old_name_and_a_bad_old_name_warns','Invalid_numeric_width_warns_and_old_names_never_supply_a_fallback')
edit('tests/Hotline.Core.Tests/ToolbarModelTests.cs',tests)
