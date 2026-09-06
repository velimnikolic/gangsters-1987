"""Keep Unity's harmless material serialization defaults when authored values match."""
import re
import yaml


def matches(existing,generated):
    if not existing:return False
    def read(text):return yaml.safe_load(re.split(r'^--- !u!\d+ &\d+\n',text,flags=re.M)[1])['Material']
    a,b=read(existing),read(generated)
    for key in ('m_Shader','m_Parent','m_Name','m_LightmapFlags','m_EnableInstancingVariants',
                'm_DoubleSidedGI','m_CustomRenderQueue','stringTagMap'):
        if a.get(key)!=b.get(key):return False
    for key in ('m_ValidKeywords','m_InvalidKeywords'):
        if set(a.get(key,[]))!=set(b.get(key,[])):return False
    actual={x.casefold() for x in a.get('disabledShaderPasses',[])}
    expected={x.casefold() for x in b.get('disabledShaderPasses',[])}
    if not expected<=actual or actual-expected-{'motionvectors'}:return False
    for group,values in b['m_SavedProperties'].items():
        have={k:v for entry in a['m_SavedProperties'].get(group,[]) for k,v in entry.items()} if isinstance(values,list) else None
        if have is None:
            if a['m_SavedProperties'].get(group)!=values:return False
        else:
            for entry in values:
                for key,value in entry.items():
                    if have.get(key)!=value:return False
    return True
