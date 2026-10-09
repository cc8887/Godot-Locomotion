"""Replacement boundaries shared by generation and publication."""
PROP_TOKENS=('rifle','pistol','bow','binocular','bino','torch','box','barrel')

def is_prop(row):
    return row['family']=='prop_animation' or any(token in row['semanticName'].lower() for token in PROP_TOKENS)

def non_prop_targets(contracts):
    return [f"asset:{r['edition']}:{r['asset']}" for r in contracts['assets'] if not is_prop(r)]
