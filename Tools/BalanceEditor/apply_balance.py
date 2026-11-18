#!/usr/bin/env python3
"""Apply Balance Editor JSON to Rubezh 0.11.29 source safely.

Every numeric value is matched at exactly one known location in the exact source
layout. Refuse unknown changed source rather than guessing and breaking C#.
"""
import argparse, datetime, json, pathlib, re, shutil, sys
from source_match import field_matches, literal_matches, replace_literal
ROOT=pathlib.Path(__file__).resolve().parents[2]
SCHEMA=json.loads((pathlib.Path(__file__).parent/'schema.json').read_text(encoding='utf-8'))
FIELDS={f['id']:f for f in SCHEMA['fields']}

def load(path):
 data=json.loads(pathlib.Path(path).read_text(encoding='utf-8-sig'))
 if data.get('format')!='rubezh-balance-profile-v1':raise ValueError('Неверный формат профиля')
 if data.get('game_version')!='0.11.29':raise ValueError('Профиль не для 0.11.29')
 return data

def validate(data):
 values=data.get('values',{})
 if not isinstance(values,dict):raise ValueError('Поле values должно быть объектом')
 changes={}
 for key,val in values.items():
  if key not in FIELDS:raise ValueError('Неизвестный параметр: '+key)
  field=FIELDS[key]
  if isinstance(val,bool) or not isinstance(val,(float,int)):raise ValueError(f'{key}: ожидалось число')
  if not field['min']<=val<=field['max']:raise ValueError(f'{key}: {val} вне диапазона')
  if field['is_int'] and (not isinstance(val,int) or int(val)!=val):raise ValueError(f'{key}: требуется целое число')
  if abs(val-field['default'])>1e-8:changes[key]=val
 merged={k:f['default'] for k,f in FIELDS.items()};merged.update(values)
 for lower,upper in [('ak_min','ak_max'),('rpg_min','rpg_max'),('ak_interval_min','ak_interval_max'),('under_dmg_min','under_dmg_max'),('hand_dmg_min','hand_dmg_max')]:
  if lower in merged and upper in merged and merged[lower]>merged[upper]:raise ValueError(f'{lower} больше {upper}')
 return changes

# Connect historical C# constants to the calculation sites that still use
# duplicated literals. These changes are made only if their related fields
# are edited. All anchors are checked before ANY source file is written.
LINKS=[
 (('hp',), 'Assets/Rubezh/Core/Logistics.cs',
  '100f-patient.Health',
  '__HP_CAP__f-patient.Health'),
 (('rpg_min','rpg_max'), 'Assets/Rubezh/Core/SpecialWeapons.cs',
  'b.Random.Range(34.15f, 54.4f)',
  'b.Random.Range(SpecialWeapons.DamageMin, SpecialWeapons.DamageMax)'),
 (('rpg_blast',), 'Assets/Rubezh/Core/SpecialWeapons.cs',
  'return (s.LauncherKind == 1) ? 4.3f : 3.3f;',
  'return (s.LauncherKind == 1) ? 4.3f : BlastRadius;'),
 (('rpg_blast','rshg_radius'), 'Assets/Rubezh/Core/ArcedOrdnance.cs',
  'Radius = ((arcProjectile.Kind == 4) ? 4.3f : 3.3f),',
  'Radius = SilentOrders.Core.SpecialWeapons.Radius(shooter),'),
 (('rpg_blast',), 'Assets/Rubezh/Core/SpecialWeapons.cs',
  'soldier.Alive && !(num > 3.3f) && b.Map.LineOfSight(impact, soldier.Position)',
  'soldier.Alive && !(num > BlastRadius) && b.Map.LineOfSight(impact, soldier.Position)'),
 (('rpg_blast',), 'Assets/Rubezh/Core/SpecialWeapons.cs',
  'ConcussionEffects.Apply(b, soldier, num, 3.3f);',
  'ConcussionEffects.Apply(b, soldier, num, BlastRadius);'),
 (('under_radius','hand_radius'), 'Assets/Rubezh/Core/ArcedOrdnance.cs',
  'float radius = ((arcProjectile.Kind == 2) ? 2.7f : 2.5f);',
  'float radius = BlastRadius(arcProjectile);'),
 (('rshg_radius',), 'Assets/Rubezh/Core/SpecialWeapons.cs',
  'SilentOrders.Core.ArcedOrdnance.Resolve(b, shooter, impact, 4.3f, 50f, 65f, -1, 0f, thermobaric: true, origin: origin);',
  'SilentOrders.Core.ArcedOrdnance.Resolve(b, shooter, impact, Radius(shooter), 50f, 65f, -1, 0f, thermobaric: true, origin: origin);')
]

def apply(project,data,dry=False):
 changes=validate(data);affected={};edits={}
 for key,new in changes.items():
  field=FIELDS[key];path=project/field['source']
  if not path.is_file():raise ValueError('Нет исходника: '+str(path))
  if path not in affected: affected[path]=path.read_text(encoding='utf-8-sig')
  text=affected[path];hits=field_matches(field['pattern'],text)
  if len(hits)!=1:raise ValueError(f'{key}: требуется ровно одно совпадение; найдено {len(hits)} в {path.name}')
  hit=hits[0];old=hit.group('num');oldnum=float(old)
  if abs(oldnum-field['default'])>1e-6:
   raise ValueError(f'{key}: исходник изменён: ожидается {field["default"]}, найдено {oldnum}. Не переписываю!')
  if field['is_int']:
   replacement=str(int(new))
  else:
   replacement=f'{float(new):.8g}'
   if '.' not in replacement and 'e' not in replacement:replacement+='.0'
  # Preserve existing C# float literal suffix.
  edits.setdefault(path,[]).append((hit.start('num'),hit.end('num'),replacement))
 for path,updates in edits.items():
  text=affected[path];last=len(text)+1
  for start,end,token in sorted(updates,reverse=True):
   if end>last:raise ValueError('Перекрывающиеся числовые параметры: '+str(path))
   text=text[:start]+token+text[end:];last=start
  affected[path]=text
 for keys,relative,old,new in LINKS:
  if not any(k in changes for k in keys):continue
  path=project/relative
  if path not in affected: affected[path]=path.read_text(encoding='utf-8-sig')
  text=affected[path]
  if new.startswith('__HP_CAP__'):
   pass # handled below when new includes the placeholder
  if '__HP_CAP__' in new:
   hp_value=f'{float(changes["hp"]):.8g}'
   if '.' not in hp_value and 'e' not in hp_value:hp_value+='.0'
   new=new.replace('__HP_CAP__',hp_value)
  if 'rshg_radius' in changes and 'return (s.LauncherKind' in old:
   rshg_val=changes['rshg_radius']
   rshg_token=f'{float(rshg_val):.8g}'
   if '.' not in rshg_token and 'e' not in rshg_token:rshg_token+='.0'
   old=old.replace('4.3f',rshg_token+'f')
   new=new.replace('4.3f',rshg_token+'f')
  if literal_matches(new,text):continue
  occurrences=len(literal_matches(old,text))
  if occurrences!=1:raise ValueError(f'Старая формула {relative} изменена или отсутствует: {old[:60]} ({occurrences})')
  affected[path]=replace_literal(text,old,new)
 print(f'Изменено параметров: {len(changes)}, файлов C#: {len(affected)}')
 if dry or not affected:
  for key,val in changes.items():print('  ',key,FIELDS[key]['default'],'->',val)
  return len(changes)
 stamp=datetime.datetime.now().strftime('%Y%m%d-%H%M%S')
 backup=project/'BalanceEditorBackup'/stamp
 backup.mkdir(parents=True,exist_ok=True)
 for path,text in affected.items():
  rel=path.relative_to(project);bak=backup/rel;bak.parent.mkdir(parents=True,exist_ok=True)
  shutil.copy2(path,bak)
  temp=path.with_suffix(path.suffix+'.balance.tmp')
  temp.write_text(text,encoding='utf-8')
  temp.replace(path)
 (backup/'profile.json').write_text(json.dumps(data,ensure_ascii=False,indent=2),encoding='utf-8')
 print('Резервные копии:',backup)
 return len(changes)

def cli():
 p=argparse.ArgumentParser(description='Применение баланса Рубеж 0.11.29')
 p.add_argument('profile',help='JSON файл, экспортированный редактором')
 p.add_argument('--project',default=str(ROOT),help='Путь к корню Unity-проекта')
 p.add_argument('--dry-run',action='store_true',help='Только проверка, не менять файлы')
 opts=p.parse_args()
 try:apply(pathlib.Path(opts.project).resolve(),load(opts.profile),opts.dry_run)
 except (ValueError,OSError,json.JSONDecodeError) as e:print('ОШИБКА:',e,file=sys.stderr);sys.exit(2)
if __name__=='__main__':cli()
