import sys
from pathlib import Path
sys.path.insert(0, r'V:\Repos\Apocalypter\partadjustment\tools')
import UnityPy, pmfsm
env = UnityPy.load(r'V:\Repos\Apocalypter\partadjustment\research\out\sharedassets1.assets')
objects = list(env.objects)
names = {o.path_id: o.read().m_Name for o in objects if o.type.name == 'GameObject'}
types={15:'fsmFloatParams',16:'fsmIntParams',17:'fsmBoolParams',18:'fsmStringParams',19:'fsmGameObjectParams',20:'fsmOwnerDefaultParams',23:'stringParams',24:'fsmObjectParams',28:'fsmVector3Params',31:'fsmEventTargetParams'}
def val(d,i):
    return d[types[d['paramDataType'][i]]][d['paramDataPos'][i]] if d['paramDataType'][i] in types else None
for obj in objects:
    if obj.type.name != 'MonoBehaviour': continue
    raw=obj.get_raw_data()
    import struct
    go_id=struct.unpack('<q',raw[4:12])[0]
    name=names.get(go_id,'')
    if not any(s in name.lower() for s in ('blastlance','explosion')): continue
    try: f=pmfsm.parse(raw)['fsm']
    except Exception: continue
    if f['name']=='ExplosionDamage' and name in ('Explosion_Can','Explosion_Grenade'):
        print(name,'variables',f['variables']['floatVariables'])
    for state in f['states']:
        d=state['actionData'];starts=d['actionStartIndex']+[len(d['paramName'])]
        for j,action in enumerate(d['actionNames']):
            if f['name']!='ExplosionDamage' and not any(action.endswith(s) for s in ('SetFsmFloat','CreateObject','FsmFloatAdd')): continue
            values={d['paramName'][i]:val(d,i) for i in range(starts[j],starts[j+1])}
            if f['name']=='ExplosionDamage' and name not in ('Explosion_Can','Explosion_Grenade'):continue
            if action.endswith('CreateObject'):
                ptr=values.get('gameObject',{}).get('value',{})
                values['created_name']=names.get(ptr.get('m_PathID'),'')
            print(name, f['name'],state['name'],action.split('.')[-1],values)
