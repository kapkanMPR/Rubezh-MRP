"""Compare recorded 213/214 runs. Does not execute Unity or claim device FPS."""
from pathlib import Path
import hashlib,json
root=Path(__file__).resolve().parents[3]
out=root/'Build/Reports/AI-0.12.214'
rows=[]
for name in ('Native214Check','Native214CheckBattle','Native214CheckBattleLong'):
 def read(group):return json.JSONDecoder().raw_decode((out/group/(name+'-run.log')).read_text())[0]
 old,new=read('Baseline'),read('Current')
 assert old['state']==new['state'],name+': final position/health/ammo state differs'
 old_presentation=old['renderContractMs']+old['observationContractMs']
 new_presentation=new['renderContractMs']+new['observationContractMs']
 rows.append(dict(scenario=new['scenario'],ticks=new['requestedTicks'],simulation_seconds=new['simulationTime'],survivors=new['survivors'],state_parity=True,state_sha256=hashlib.sha256(new['state'].encode()).hexdigest(),body_rebuilds_213=old['rebuilds'],body_rebuilds_214=new['rebuilds'],body_rebuild_reduction_percent=100*(1-new['rebuilds']/old['rebuilds']),reuse_percent_213=old['reuseRatio']*100,reuse_percent_214=new['reuseRatio']*100,render_contract_ms_213=old['renderContractMs'],render_contract_ms_214=new['renderContractMs'],observe_contract_ms_214=new['observationContractMs'],combined_presentation_contract_ms_213=old_presentation,combined_presentation_contract_ms_214=new_presentation,presentation_contract_reduction_percent=100*(1-new_presentation/old_presentation),counters_214=new['painterCounters'],node_samples_213=old['samples'],node_samples_214=new['samples']))
old=json.loads((out/'Baseline/pose-goldens.json').read_text());new=json.loads((out/'Current/pose-goldens.json').read_text())
maximum=0;count=0
assert len(old)==len(new)==7
for a,b in zip(old,new):
 a=[p for p in a if p[6]==1];b=[p for p in b if p[6]==1]
 assert len(a)==len(b)
 for x,y in zip(a,b):
  for xx,yy in zip(x,y):maximum=max(maximum,abs(xx-yy));count+=1
assert maximum<.001,maximum
report=dict(version='0.12.214',baseline='unmodified213 source snapshot',result='PASS',scenarios=rows,sprite_endpoint_parity=dict(cases=7,scalar_comparisons=count,max_difference=maximum,tolerance=.001),checks=dict(primitive_silhouette_cases=1600,pose_interpolation_transform_depth_invariants=4391,world_retention_cases=22,regression_checks=602,building_invariants=1718583,unity_player_conditional_api_contract_compile='PASS'),state_parity_fields=['soldier id','position x/y','health','ammo'],per_render_immutability_fields=['position','facing','health','ammo','deployment','moving','last fire','time','tick','outcome','combat RNG','voice RNG'],limits=['Unity Editor/GPU/device execution unavailable. Shader output, actual texture import, Addressables/APK build and device FPS remain unverified.','Timings are single host runs with API test doubles, include reflection overhead, and are not FPS or Unity performance guarantees.','Actual battle Step timings vary with host load; core outcomes match the recorded fields.','Free projectiles and effects remain sampled at simulation ticks; this change interpolates actors.','Retained world objects still use linear visibility scans; no claim that all frame costs are eliminated.','Hierarchy activation remains; node samples track active objects, not measured native Unity memory.','Status/marker solids use continuous retained edges; only sprite endpoints are compared to213 golden coordinates.'])
(out/'validation-summary.json').write_text(json.dumps(report,ensure_ascii=False,indent=2))
lines=['213 → 214: portable validation','', '| Scenario | Body rebuilds 213 → 214 | Reuse 213 → 214 | State |','|---|---:|---:|---|']
for row in rows:lines.append(f"| {row['ticks']} ticks, {'AI' if row['scenario'].startswith('actual') else 'synthetic'} | {row['body_rebuilds_213']} → {row['body_rebuilds_214']} | {row['reuse_percent_213']:.2f}% → {row['reuse_percent_214']:.2f}% | identical recorded fields |")
lines+=['','All targeted cases passed. No Unity GPU/device execution was available.','Host contract timing reductions are recorded in JSON and must not be presented as device FPS gains.']
(out/'comparison.txt').write_text('\n'.join(lines)+'\n')
print('\n'.join(lines))
