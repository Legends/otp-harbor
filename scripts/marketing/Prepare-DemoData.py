"""Deterministic fictional demo accounts and original geometric icon pack for Debug builds."""
from __future__ import annotations
import hashlib, io, json, uuid, zipfile
from pathlib import Path
from PIL import Image, ImageDraw

ROOT = Path(__file__).resolve().parents[2] / 'artifacts/marketing/demo-data'
BRANDS = [
 ('Qelvorith','qelvorith','#4964A8'), ('Vunqelora','vunqelora','#8A5A38'),
 ('Zevqoralis','zevqoralis','#0B8076'), ('Fenqaviel','fenqaviel','#7159A8'),
 ('Noxaveliq','noxaveliq','#B35E4A'), ('Tavelqora','tavelqora','#487A91'),
 ('Brinovexel','brinovexel','#9E4E77'), ('Cerynqel','cerynqel','#577A56'),
 ('Dravqenor','dravqenor','#826E43'), ('Elyqorun','elyqorun','#3D7B88'),
 ('Fexavoryn','fexavoryn','#926376'), ('Grynolith','grynolith','#586BB0'),
 ('Havorqen','havorqen','#577A6F'), ('Jexaloria','jexaloria','#8D694A'),
 ('Kynoverix','kynoverix','#6857A2'), ('Lurqavena','lurqavena','#417A83'),
 ('Merviqel','merviqel','#A25C5F'), ('Nyrathove','nyrathove','#47718A'),
 ('Orvexalin','orvexalin','#7B6851'), ('Pynqelith','pynqelith','#A04B81'),
 ('Ravolqith','ravolqith','#557B49'), ('Selyqavor','selyqavor','#6B63A8'),
 ('Tynovelq','tynovelq','#9A7051'), ('Varyqenor','varyqenor','#507BA2'),
 ('Wexarune','wexarune','#987055'), ('Zolqavith','zolqavith','#8B5F99')]
ROOT.mkdir(parents=True, exist_ok=True)
(ROOT/'icons').mkdir(exist_ok=True)
for stale_master in (ROOT/'icons').glob('*-master-512.png'):
    stale_master.unlink()
accounts=[]; brands=[]; assets={}
for index,(name,slug,color) in enumerate(BRANDS):
    # Original abstract marks use thirteen different geometric families; the second
    # set changes each construction as well as its color. No source artwork is copied.
    image=Image.new('RGBA',(512,512),(0,0,0,0)); d=ImageDraw.Draw(image)
    rgb=tuple(bytes.fromhex(color[1:])); d.rounded_rectangle((16,16,496,496),radius=112,fill=rgb)
    white='#F8F7F1'; family=index%13; variant=index//13
    if family==0:  # staggered channels
        for row in range(3+variant):
            x=130+(row%2)*55; y=154+row*(58 if variant else 74)
            d.rounded_rectangle((x,y,x+220-(row%2)*50,y+20),radius=10,fill=white)
    elif family==1:  # twin diamonds
        for r in (92,158):
            points=[(256,256-r),(256+r,256),(256,256+r),(256-r,256)]
            d.line(points+[points[0]],fill=white,width=18 if variant else 24,joint='curve')
        if variant: d.ellipse((236,236,276,276),fill=white)
    elif family==2:  # offset ring and satellite
        d.arc((124,124,388,388),25+variant*35,295+variant*25,fill=white,width=26)
        d.ellipse((337,146,383,192) if variant else (135,322,181,368),fill=white)
        d.ellipse((230,230,282,282),fill=white)
    elif family==3:  # rising modules
        for k in range(4):
            x=126+k*73; y=310-k*53 if not variant else 151+k*53
            d.rounded_rectangle((x,y,x+53,y+53),radius=11,fill=white)
    elif family==4:  # chevron ribbons
        for k in range(2+variant):
            y=163+k*(94 if not variant else 68)
            d.line([(144,y),(256,y+60),(368,y)],fill=white,width=25,joint='curve')
    elif family==5:  # parallel wave arcs
        for k in range(3):
            x=121+k*80
            d.arc((x,137,x+110,380),70 if variant else 250,300 if variant else 480,fill=white,width=22)
    elif family==6:  # nested orbit
        d.ellipse((126,126,386,386),outline=white,width=22)
        d.ellipse((185,185,327,327),outline=white,width=20)
        d.ellipse((277,154,326,203) if variant else (190,308,239,357),fill=white)
    elif family==7:  # petal cluster
        import math
        for k in range(5+variant):
            a=k*2*math.pi/(5+variant); x=256+math.cos(a)*93; y=256+math.sin(a)*93
            d.ellipse((x-27,y-27,x+27,y+27),fill=white)
        d.ellipse((234,234,278,278),fill=white)
    elif family==8:  # hourglass / faceted joint
        d.polygon([(143,153),(369,153),(290,246),(222,246)],fill=white)
        d.polygon([(222,266),(290,266),(369,359),(143,359)],fill=white)
        if variant: d.rectangle((240,244,272,268),fill=white)
    elif family==9:  # peaks
        for k in range(3):
            x=126+k*86; top=160+(k%2)*48 if not variant else 210-(k%2)*48
            d.polygon([(x,348),(x+38,top),(x+76,348)],fill=white)
    elif family==10:  # modular lattice
        for row in range(3):
            for col in range(3):
                if (row+col+variant)%3!=0:
                    x=145+col*81; y=145+row*81
                    d.rounded_rectangle((x,y,x+48,y+48),radius=10,fill=white)
    elif family==11:  # split rails
        d.rounded_rectangle((137,156,187,355),radius=20,fill=white)
        d.rounded_rectangle((325,156,375,355),radius=20,fill=white)
        d.line([(184,332),(325,178)] if variant else [(184,178),(325,332)],fill=white,width=29)
    else:  # broken circle of pearls
        import math
        for k in range(7+variant):
            a=k*2*math.pi/(7+variant); x=256+math.cos(a)*130; y=256+math.sin(a)*130
            d.ellipse((x-17,y-17,x+17,y+17),fill=white)
        d.ellipse((226,226,286,286),fill=white)
    image.save(ROOT/'icons'/f'{slug}-master-512.png',optimize=True)
    # The runtime contract requires 128px PNG assets; preserve 512px masters separately.
    runtime=image.resize((128,128),Image.Resampling.LANCZOS)
    buffer=io.BytesIO(); runtime.save(buffer,format='PNG',optimize=True)
    png=buffer.getvalue(); digest=hashlib.sha256(png).hexdigest()
    path=f'runtime/appearance-128/{digest}.png'; assets[path]=png
    ref={'provider':'otp-harbor-demo','sourceId':slug,'metadata':{}}
    appearance={'asset':{'path':path,'mediaType':'image/png','width':128,'height':128,
        'sha256':digest,'renderProfile':'otp-harbor-themed-appearance-v1'},
        'assetRole':'complete-icon','background':{'mode':'embedded'},
        'selection':'complete-source-artwork',
        'source':{'provider':'otp-harbor-demo','sourceId':slug,'assetPath':f'icons/{slug}-master-512.png'},
        'appearanceSelectionReason':'Original project-created geometric demo icon.'}
    brands.append({'id':slug,'displayName':name,'issuerAliases':[name],
        'selectedSource':ref,'sources':[ref],
        'appearances':{'lightTheme':appearance,'darkTheme':appearance}})
    for number in range(1):
        seed=hashlib.sha256(f'OTP Harbor fictional demo {slug} {number}'.encode()).digest()[:20]
        import base64
        accounts.append({'id':str(uuid.uuid5(uuid.NAMESPACE_DNS,f'otp-harbor-demo:{slug}:{number}')),
            'issuer':name,'secret':base64.b32encode(seed).decode().rstrip('='),
            'account_name':f'capture{number+1}@example.{"com" if number==0 else "org"}',
            'period':30,'groups':[]})
(ROOT/'otp-harbor-demo-accounts.json').write_text(json.dumps(accounts,indent=2)+'\n',encoding='utf-8')
notice=b'Original geometric demo icons drawn by the OTP Harbor marketing generator. No third-party assets are included. Brand names require human trademark review before publication.\n'
manifest={'formatVersion':3,'packId':'otp-harbor-icons','name':'OTP Harbor Fictional Demo Icons',
    'sources':[{'provider':'otp-harbor-demo','inputFileName':'Prepare-DemoData.py',
        'sha256':hashlib.sha256(Path(__file__).read_bytes()).hexdigest(),'version':'1','revision':None,'sourceUrl':None,
        'metadata':{'origin':'project-created fictional demo artwork'},
        'licenseFiles':['licenses/otp-harbor-demo/NOTICE.txt']}],
    'brands':brands,'issuerAliases':[{'key':name.lower(),'brandId':slug} for name,slug,_ in BRANDS]}
with zipfile.ZipFile(ROOT/'otp-harbor-demo-icons.otphicons','w',compression=zipfile.ZIP_DEFLATED) as z:
    for path,data in sorted({'pack.json':(json.dumps(manifest,indent=2)+'\n').encode(),
            'licenses/otp-harbor-demo/NOTICE.txt':notice,**assets}.items()):
        z.writestr(path,data)
release_manifest_path=ROOT.parent/'manifest.json'
if release_manifest_path.is_file():
    release_manifest=json.loads(release_manifest_path.read_text(encoding='utf-8'))
    release_manifest['demoAccountsSha256']=hashlib.sha256(
        (ROOT/'otp-harbor-demo-accounts.json').read_bytes()).hexdigest()
    release_manifest['demoIconPackSha256']=hashlib.sha256(
        (ROOT/'otp-harbor-demo-icons.otphicons').read_bytes()).hexdigest()
    release_manifest['demoIconMasters']=[{
        'path':path.relative_to(ROOT.parent).as_posix(),
        'sha256':hashlib.sha256(path.read_bytes()).hexdigest(),
        'kind':'project-created-fictional-demo-icon-master',
    } for path in sorted((ROOT/'icons').glob('*-master-512.png'))]
    release_manifest_path.write_text(
        json.dumps(release_manifest,indent=2)+'\n',encoding='utf-8')
print(f'Generated {len(accounts)} synthetic accounts and {len(brands)} original geometric icons.')
