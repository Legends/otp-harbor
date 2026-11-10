"""Validate fictional demo inputs and recorded marketing images before publication."""
import argparse, hashlib, json, re, sys, zipfile
from pathlib import Path
from PIL import Image, ImageChops, ImageDraw, ImageOps, ImageStat
import zxingcpp
from screenshot_cleanup import rounded_alpha
ROOT=Path(__file__).resolve().parents[2]/'artifacts/marketing'
def normalized_text_sha256(path):
    text=path.read_text(encoding='utf-8').replace('\r\n','\n').replace('\r','\n')
    return hashlib.sha256(text.encode('utf-8')).hexdigest()
def file_sha256(path):
    return normalized_text_sha256(path) if path.suffix.lower()=='.json' else hashlib.sha256(path.read_bytes()).hexdigest()
parser=argparse.ArgumentParser(); parser.add_argument('--require-captures',action='store_true')
args=parser.parse_args()
accounts=json.loads((ROOT/'demo-data/otp-harbor-demo-accounts.json').read_text(encoding='utf-8'))
manifest=json.loads((ROOT/'manifest.json').read_text(encoding='utf-8'))
assert normalized_text_sha256(ROOT/'demo-data/otp-harbor-demo-accounts.json')==manifest['demoAccountsSha256']
assert hashlib.sha256((ROOT/'demo-data/otp-harbor-demo-icons.otphicons').read_bytes()).hexdigest()==manifest['demoIconPackSha256']
masters=manifest.get('demoIconMasters',[])
assert len(masters)==26 and len({item['path'] for item in masters})==26
for item in masters:
    master=ROOT/item['path']
    assert item['kind']=='project-created-fictional-demo-icon-master'
    assert master.parent==ROOT/'demo-data/icons' and master.name.endswith('-master-512.png')
    assert master.is_file() and hashlib.sha256(master.read_bytes()).hexdigest()==item['sha256']
with zipfile.ZipFile(ROOT/'demo-data/otp-harbor-demo-icons.otphicons') as archive:
    pack=json.loads(archive.read('pack.json'))
    assert pack['formatVersion']==3 and pack['packId']=='otp-harbor-icons'
    icons=pack['brands']; assert len(icons)==26
    assert len({b['id'] for b in icons})==26
    assert len({b['appearances']['lightTheme']['asset']['sha256'] for b in icons})==26
    for icon in icons:
        for theme in ('lightTheme','darkTheme'):
            asset=icon['appearances'][theme]['asset']
            assert hashlib.sha256(archive.read(asset['path'])).hexdigest()==asset['sha256']
assert len(accounts)==26 and len({a['id'] for a in accounts})==26
assert len({a['issuer'] for a in accounts})==26
assert {a['issuer'] for a in accounts}=={b['displayName'] for b in icons}
for entry in manifest['captures']+manifest['generated']:
    text=(json.dumps(entry,ensure_ascii=False)
          .replace('https://legends.github.io/otp-harbor/','PROJECT_WEBSITE_URL')
          .replace('https://github.com/Legends/otp-harbor','PROJECT_REPOSITORY_URL'))
    assert not re.search(r'(?i)(?:gmail|outlook|microsoft|google|github|proton|simple.icons|aegis|dashboard.icons)',text),f'Known third-party name in manifest: {entry["path"]}'
    assert not re.search(r'(?i)(?:[a-z0-9._%+-]+@(?!example\.(?:com|org|net))[^\s"/]+|otpauth://|BEGIN PRIVATE KEY|password\s*[:=])',text),f'Sensitive pattern in manifest: {entry["path"]}'
for account in accounts:
    assert re.fullmatch(r'capture\d+@example\.(com|org|net)',account['account_name'])
    assert re.fullmatch(r'[A-Z2-7]{32}',account['secret'])
    assert account['period']==30
files=list((ROOT/'source').rglob('*.png')) if (ROOT/'source').exists() else []
if args.require_captures:
    for platform in ('desktop','android'):
        for theme in ('light','dark'):
            assert (ROOT/'source'/platform/theme/'account-list.png').is_file(),f'Missing {platform} {theme} account list'
for path in files:
    assert path.parent.name in ('light','dark')
    assert path.parent.parent.name in ('desktop','android')
    with Image.open(path) as source_image:
        assert not source_image.getexif(),f'EXIF metadata in capture: {path}'
        image=source_image.convert('RGB')
        assert min(image.size)>=360 and max(ImageStat.Stat(image).stddev)>8,f'Blank or tiny: {path}'
        assert 0.5<image.width/image.height<3 if path.parent.parent.name=='desktop' else image.width/image.height<1
if files:
    recorded={item['path'] for item in manifest['captures']}
    assert {p.relative_to(ROOT).as_posix() for p in files}==recorded,'Manifest and captures differ'
    for item in manifest['captures']:
        p=ROOT/item['path']; assert hashlib.sha256(p.read_bytes()).hexdigest()==item['sha256']
        with Image.open(p) as image:
            assert (image.width,image.height)==(item['width'],item['height'])
    for platform in ('desktop','android'):
        pair=[ROOT/'source'/platform/theme/'account-list.png' for theme in ('light','dark')]
        if all(path.is_file() for path in pair):
            with Image.open(pair[0]) as light,Image.open(pair[1]) as dark:
                assert light.size==dark.size,f'{platform} theme capture dimensions differ'
assert not manifest['generated'] or files,'Generated images lack source captures'
source_hashes={item['sha256'] for item in manifest['captures']}
app_icon=ROOT.parents[1]/'OtpHarbor.UI.Avalonia.Desktop/Assets/Icons/app-1024.png'
source_hashes.add(hashlib.sha256(app_icon.read_bytes()).hexdigest())
campaign_background=ROOT.parents[1]/'packaging/windows-store/assets/source/store-marketing-screenshot-background.png'
source_hashes.add(hashlib.sha256(campaign_background.read_bytes()).hexdigest())
android_background=ROOT.parents[1]/'packaging/android/marketing/source/android-marketing-background.png'
source_hashes.add(hashlib.sha256(android_background.read_bytes()).hexdigest())
onboarding_root=ROOT.parents[1]/'OtpHarbor.UI.Avalonia.Shared/Assets/Onboarding'
onboarding_art={
    'organize-accounts.png','icon-pack.png','move-accounts.png','add-account.png',
}
for name in onboarding_art:
    source_hashes.add(hashlib.sha256((onboarding_root/name).read_bytes()).hexdigest())
source_hashes.update(item['sha256'] for item in manifest['generated'] if item['kind'] in (
    'cleaned-website-capture', 'designed-website-campaign',
    'designed-scanner-qr', 'transparent-biometric-cutout'))
for item in manifest['generated']:
    path=ROOT/item['path']
    assert path.is_file(),f'Missing generated image: {path}'
    assert file_sha256(path)==item['sha256'],f'Stale generated image: {path}'
    assert item.get('sourceSha256') and set(item['sourceSha256'])<=source_hashes,f'Unknown source for {path}'
    if item['kind']=='website-preview':
        full=ROOT/item['fullPath']
        assert item['sourceSha256']==[file_sha256(full)]
        assert item['width']==1200 and item['height']==675 and item['quality']==95
        assert path.suffix=='.webp' and item['path'].startswith('website/optimized/previews/')
        with Image.open(path) as image, Image.open(full) as original:
            assert image.format=='WEBP' and image.size==(1200,675) and not image.getexif()
            assert original.size==(1920,1080) and path.stat().st_size<full.stat().st_size
            expected=original.convert('RGB').resize(image.size,Image.Resampling.LANCZOS)
            # Catch accidental palette/resize/quality regressions, not just metadata drift.
            difference=ImageChops.difference(image.convert('RGB'),expected)
            assert sum(ImageStat.Stat(difference).mean)/3<6,f'Preview quality degraded: {path}'
            if path.stem=='qr-scanning':
                result=zxingcpp.read_barcode(image)
                assert result and result.text=='https://github.com/Legends/otp-harbor',f'Preview QR cannot be scanned: {path}'
    if item['kind']=='readme-theme-frame':
        source=ROOT/item['sourcePath']
        assert item['sourceSha256']==[file_sha256(source)] and item['padding']==32
        with Image.open(source) as original, Image.open(path) as actual:
            expected=Image.new('RGBA',(original.width+64,original.height+64),'#dce3ed')
            expected.alpha_composite(original.convert('RGBA'),(32,32))
            assert actual.size==expected.size and actual.convert('RGB').tobytes()==expected.convert('RGB').tobytes()
    if item['kind']=='cleaned-website-capture':
        capture=ROOT/item['sourcePath']
        left,top,right,bottom=item['sourceCrop']
        with Image.open(capture) as original, Image.open(path) as cleaned:
            assert cleaned.size==(right-left,bottom-top)
            assert cleaned.convert('RGB').tobytes()==original.crop((left,top,right,bottom)).convert('RGB').tobytes(),f'Cleaned app pixels changed: {path}'
            if item['platform']=='desktop':
                assert cleaned.mode=='RGBA' and cleaned.getpixel((0,0))[3]==0
                assert cleaned.getpixel((cleaned.width//2,cleaned.height//2))[3]==255
    if item['kind']=='designed-scanner-repo-qr':
        assert item['platform'] in ('desktop','android') and item['qrPayload']=='https://github.com/Legends/otp-harbor'
        source=ROOT/item['sourcePath']
        left,top,right,bottom=item['sourceCrop']
        x1,y1,x2,y2=item['qrBounds']
        art_bounds=tuple(item['artBounds'])
        with Image.open(source) as original, Image.open(path) as composite:
            expected=original.crop((left,top,right,bottom)).convert('RGB')
            assert composite.size==expected.size
            if item['platform']=='android':
                assert (x1,y1,x2,y2)==(305,950,915,1560) and art_bounds==(x1,y1,x2,y2)
            else:
                expected_art_bounds=((372,443,1023,811) if item['theme']=='light'
                                     else (522,448,874,808))
                assert (x1,y1,x2,y2)==(563,493,833,763) and art_bounds==expected_art_bounds
            difference=ImageChops.difference(composite.convert('RGB'),expected)
            difference.paste((0,0,0),art_bounds)
            assert difference.getbbox() is None,f'QR composite changed pixels outside scanner artwork: {path}'
            result=zxingcpp.read_barcode(composite)
            assert result and result.text==item['qrPayload'],f'Demo QR cannot be decoded: {path}'
    if item['kind']=='designed-website-campaign':
        capture=ROOT/item['sourcePath']
        cleaned_path=ROOT/item['cleanedPath']
        expected_background=(campaign_background if item['platform']=='desktop' else android_background)
        assert item['backgroundPath']==expected_background.relative_to(ROOT.parents[1]).as_posix()
        illustration=(ROOT.parents[1]/item['illustrationPath']) if 'illustrationPath' in item else None
        parents=[capture,expected_background,app_icon]+([illustration] if illustration else [])
        assert item['sourceSha256']==[hashlib.sha256(p.read_bytes()).hexdigest() for p in parents]
        if illustration:
            assert illustration.parent==onboarding_root and illustration.name=='move-accounts.png'
            assert item['visualKind']=='marketing-promotion'
        x,y,width,height=item['appPlacement']['bounds']
        assert 25<=x<x+width<1895 and 25<=y<y+height<1055
        with Image.open(path) as framed, Image.open(cleaned_path) as cleaned:
            assert framed.size==(1920,1080)
            expected=cleaned.convert('RGB').resize((width,height),Image.Resampling.LANCZOS)
            actual=framed.crop((x,y,x+width,y+height)).convert('RGB')
            assert actual.crop((25,25,width-25,height-25)).tobytes()==expected.crop((25,25,width-25,height-25)).tobytes(),f'Framed app interior changed: {path}'
            assert item['appPlacement']['frame']==('designed phone frame' if item['platform']=='android' else 'cleaned desktop window')
            if 'qrCompositePath' in item:
                assert item['qrCompositePath']==item['cleanedPath']
                assert item['qrPayload']=='https://github.com/Legends/otp-harbor'
                result=zxingcpp.read_barcode(framed)
                assert result and result.text==item['qrPayload'],f'Framed demo QR cannot be decoded: {path}'
    if item['kind']=='genuine-store-screenshot':
        assert item['path'].startswith('store/windows/screenshots/')
        assert path.suffix.lower()=='.png' and path.stat().st_size<=50_000_000
        assert item['sha256'] in source_hashes,f'Store screenshot is not an original capture: {path}'
        with Image.open(path) as image:
            assert image.width>=1366 and image.height>=768,f'Store screenshot too small: {path}'
    if item['kind']=='framed-store-screenshot-candidate':
        assert item['path'].startswith('store/windows/screenshots/') and path.suffix.lower()=='.png'
        assert path.stat().st_size<=50_000_000
        assert len(item['sourceSha256'])==1
        source_entry=next(c for c in manifest['captures'] if c['sha256']==item['sourceSha256'][0])
        assert source_entry['platform']=='desktop'
        x,y,width,height=item['nativeAppBounds']
        left,top,right,bottom=item['sourceCrop']
        with Image.open(path) as output, Image.open(ROOT/source_entry['path']) as original:
            assert output.width>=1366 and output.height>=768 and output.width!=output.height
            assert (width,height)==(right-left,bottom-top)
            background='#e8eef7' if path.parent.name=='light' else '#071121'
            expected=Image.new('RGB',(width,height),background)
            expected.paste(original.crop((left,top,right,bottom)).convert('RGBA'),
                           (0,0),rounded_alpha((width,height),item['cornerMaskRadius']))
            assert output.crop((x,y,x+width,y+height)).convert('RGB').tobytes()==expected.tobytes(),f'App pixels changed in Store frame: {path}'
    if item['kind']=='store-app-tile-icon':
        assert item['sourcePath']=='OtpHarbor.UI.Avalonia.Desktop/Assets/Icons/app-1024.png'
        with Image.open(path) as image:
            assert image.size==(300,300) and image.mode=='RGBA',f'Invalid Store app icon: {path}'
    if item['kind']=='designed-store-campaign':
        assert item['path'].startswith('store/windows/promotional/') and path.suffix.lower()=='.png'
        assert item['locale'] in ('en-US','de-DE','fr-FR','es-ES')
        assert path.stat().st_size<=50_000_000
        assert item['backgroundPath']=='packaging/windows-store/assets/source/store-marketing-screenshot-background.png'
        with Image.open(path) as output:
            assert output.size==(1920,1080)
            for placement in item['appCapturePlacements']:
                capture=ROOT/placement['sourcePath']
                assert capture.is_file() and capture.parent.name in ('dark','light','desktop')
                left,top,right,bottom=placement['sourceCrop']
                x,y,width,height=placement['appBounds']
                with Image.open(capture) as original:
                    expected=original.crop((left,top,right,bottom)).convert('RGB').resize((width,height),Image.Resampling.LANCZOS)
                actual=output.crop((x,y,x+width,y+height)).convert('RGB')
                if 'cornerMaskRadius' in placement:
                    mask=Image.new('L',(width,height))
                    ImageDraw.Draw(mask).rounded_rectangle((0,0,width-1,height-1),radius=placement['cornerMaskRadius'],fill=255)
                    difference=ImageChops.difference(actual,expected)
                    assert not ImageChops.multiply(difference,mask.convert('RGB')).getbbox(),f'Campaign changed interior app pixels: {path}'
                else:
                    assert actual.tobytes()==expected.tobytes(),f'Campaign changed app pixels: {path}'
            for placement in item.get('illustrationPlacements',[]):
                illustration=ROOT.parents[1]/placement['sourcePath']
                assert illustration.parent==onboarding_root and illustration.name in onboarding_art
                assert illustration.is_file()
                x,y,width,height=placement['bounds']
                with Image.open(illustration) as original:
                    assert width<=original.width and height<=original.height,f'Illustration upscaled: {path}'
                assert 0<=x<x+width<=1920 and 0<=y<y+height<=1080
    if item['kind']=='android-store-website-campaign':
        assert item['path'].startswith('store/android/website-campaign/en-US/')
        source=ROOT/item['sourcePath']
        assert item['sourceSha256']==[hashlib.sha256(source.read_bytes()).hexdigest()]
        assert path.read_bytes()==source.read_bytes(),f'Android campaign mirror changed pixels: {path}'
        with Image.open(path) as output:
            assert output.size==(1920,1080) and output.mode=='RGB'
    if item['kind']=='android-store-phone-screenshot':
        assert item['path'].startswith('store/android/phone-screenshots/')
        source=ROOT/item['sourcePath']
        assert item['sourceSha256']==[hashlib.sha256(source.read_bytes()).hexdigest()]
        x,y,width,height=item['appBounds']
        assert 0<=x<x+width<=1080 and 0<=y<y+height<=1920
        with Image.open(source) as original, Image.open(path) as output:
            source_pixels=original.convert('RGB')
            scale=min(1080/source_pixels.width,1920/source_pixels.height,1)
            expected_size=(round(source_pixels.width*scale),round(source_pixels.height*scale))
            assert (width,height)==expected_size and output.size==(1080,1920) and output.mode=='RGB'
            expected=Image.new('RGB',(1080,1920),item['background'])
            expected.paste(source_pixels.resize(expected_size,Image.Resampling.LANCZOS),(x,y))
            assert output.tobytes()==expected.tobytes(),f'Android phone screenshot is stale: {path}'
    if item['kind']=='android-store-feature-graphic':
        assert item['path']=='store/android/featureGraphic.png'
        source=ROOT/item['sourcePath']
        assert item['sourceSha256']==[hashlib.sha256(source.read_bytes()).hexdigest()]
        with Image.open(source) as original, Image.open(path) as output:
            expected=ImageOps.fit(original.convert('RGB'),(1024,500),method=Image.Resampling.LANCZOS)
            assert output.size==(1024,500) and output.mode=='RGB'
            assert output.tobytes()==expected.tobytes(),f'Android feature graphic is stale: {path}'
    if item['kind']=='android-store-high-resolution-icon':
        assert item['path']=='store/android/icon.png'
        assert item['sourcePath']=='OtpHarbor.UI.Avalonia.Desktop/Assets/Icons/app-1024.png'
        assert item['sourceSha256']==[hashlib.sha256(app_icon.read_bytes()).hexdigest()]
        with Image.open(app_icon) as original, Image.open(path) as output:
            expected=original.convert('RGBA').resize((512,512),Image.Resampling.LANCZOS)
            assert output.size==(512,512) and output.mode=='RGBA'
            assert output.tobytes()==expected.tobytes(),f'Android high-resolution icon is stale: {path}'
            if item.get('visualKind')=='conceptual-promotion':
                assert item['path'].split('/')[-1][:2] in ('08','09','10','11')
                assert not item['appCapturePlacements'] and item['illustrationPlacements']
                expected_art={
                    '08-groups-favourites.png':'organize-accounts.png',
                    '09-icon-pack.png':'icon-pack.png',
                    '10-backup-restore.png':'move-accounts.png',
                    '11-import-formats.png':'add-account.png',
                }
                assert [Path(placement['sourcePath']).name for placement in item['illustrationPlacements']]==[
                    expected_art[path.name]],f'Unexpected onboarding illustration: {path}'
            for placement in item.get('cutoutPlacements',[]):
                cutout=ROOT/placement['sourcePath']
                assert cutout.is_file() and cutout.name=='biometric-prompt-dark-transparent.png'
                x,y,width,height=placement['bounds']
                with Image.open(cutout) as original:
                    assert original.mode=='RGBA' and original.getpixel((0,0))[3]==0
                    assert original.width>=width and original.height>=height
                assert 0<=x<x+width<=1920 and 0<=y<y+height<=1080
    if item['kind']=='designed-scanner-qr':
        with Image.open(path) as image:
            result=zxingcpp.read_barcode(image)
            assert result and result.text==item['qrPayload']=='https://legends.github.io/otp-harbor/',f'QR decode failed: {path}'
            if item['platform']=='desktop':
                assert image.size==(1394,1044) and item['sourceCrop']==[3,3,1397,1047]
                assert item['artBounds']==[372,444,1023,810]
                with Image.open(ROOT/'source'/'desktop'/item['theme']/'scanner.png') as original:
                    expected=original.crop(tuple(item['sourceCrop'])).convert('RGB')
                difference=ImageChops.difference(image.convert('RGB'),expected)
                difference.paste((0,0,0),tuple(item['artBounds']))
                assert difference.getbbox() is None,f'Store scanner composite changed app pixels outside QR artwork: {path}'
    if item['kind']=='transparent-biometric-cutout':
        with Image.open(path) as image:
            assert image.mode=='RGBA' and image.getpixel((0,0))[3]==0,f'Cutout lacks transparency: {path}'
for item in manifest.get('storeUploadOrder',[]):
    assert (ROOT/item['path']).is_file() and len(item['caption'])<=200
for item in manifest.get('storeRecommendedOrder',[]):
    assert item['path'].startswith('store/windows/screenshots/') and len(item['caption'])<=200
    assert item['status'] in ('ready for manual review','awaiting eligible genuine capture')
assert len(manifest.get('storeUploadOrder',[]))<=10
for culture,items in manifest.get('storeCampaignOrder',{}).items():
    assert culture in ('en-US','de-DE','fr-FR','es-ES') and len(items)==10
    if args.require_captures:
        assert all(item['status'].startswith('branded') for item in items),f'Incomplete Store campaign: {culture}'
    assert items[0]['path'].endswith('/02-dark-account-list.png')
    assert items[1]['path'].endswith('/03-light-dark-themes.png')
    assert [Path(item['path']).name for item in items[6:10]]==[
        '08-groups-favourites.png','09-icon-pack.png','10-backup-restore.png','11-import-formats.png']
    assert not (ROOT/f'store/windows/promotional/{culture}/01-light-account-list.png').exists()
    for item in items:
        assert len(item['caption'])<=200
        assert item['status'] in ('branded composite candidate; manual and Partner Center review required','awaiting genuine Dark settings capture')
        assert (ROOT/item['path']).is_file() if item['status'].startswith('branded') else not (ROOT/item['path']).exists()
    with Image.open(ROOT/f'store/windows/promotional/{culture}/07-qr-import.png') as qr_slide:
        result=zxingcpp.read_barcode(qr_slide)
        assert result and result.text=='https://legends.github.io/otp-harbor/',f'Campaign QR cannot be scanned: {culture}'
expected_android_campaign=[
    '01-dark-account-list.png','02-dark-settings.png','03-dark-qr-scanning.png',
    '04-dark-backup-restore.png','05-light-account-list.png','06-light-settings.png',
    '07-light-qr-scanning.png','08-light-backup-restore.png',
]
android_campaign=manifest.get('androidWebsiteCampaignOrder',[])
assert [Path(item['path']).name for item in android_campaign]==expected_android_campaign
assert all(item['path'].startswith('store/android/website-campaign/en-US/') for item in android_campaign)
expected_android_phones=[
    '01-dark-account-list-1080x1920.png','02-dark-settings-1080x1920.png',
    '03-dark-scanner-1080x1920.png','04-light-account-list-1080x1920.png',
    '05-light-settings-1080x1920.png','06-light-scanner-1080x1920.png',
]
android_phones=manifest.get('androidPhoneScreenshotOrder',[])
assert [Path(item['path']).name for item in android_phones]==expected_android_phones
assert all(item['path'].startswith('store/android/phone-screenshots/') for item in android_phones)
gallery_path=ROOT/'website/gallery.json'
if gallery_path.exists():
    gallery=json.loads(gallery_path.read_text(encoding='utf-8'))
    assert gallery.get('schemaVersion')==2,'Gallery requires separate preview/full assets'
    expected_gallery={
        ('desktop','dark'):['account-list','account-context-menu','settings','import-formats','qr-scanning','backup-restore'],
        ('desktop','light'):['account-list','settings','import-formats','qr-scanning','backup-restore'],
        ('android','dark'):['account-list','settings','qr-scanning','backup-restore'],
        ('android','light'):['account-list','settings','qr-scanning','backup-restore'],
    }
    expected_titles={
        'account-list':'Account List',
        'account-context-menu':'Account Context Menu',
        'settings':'Settings',
        'import-formats':'Import Formats',
        'qr-scanning':'QR Scanning',
        'backup-restore':'Backup and Restore',
    }
    for (platform,theme),screens in expected_gallery.items():
        actual=[item['screen'] for item in gallery['items'] if item['platform']==platform and item['theme']==theme]
        assert actual==screens,f'Unexpected {platform} {theme} gallery order: {actual}'
    for item in gallery['items']:
        assert item['alt'] and item['caption'] and item['platform'] in ('desktop','android')
        assert item['theme'] in ('light','dark')
        assert 'src' not in item,'Ambiguous legacy gallery source'
        assert item['caption']==expected_titles[item['screen']],f'Unexpected gallery title: {item}'
        for kind,folder,suffix,dimensions in (('preview','previews','.webp',(1200,675)),('full','framed','.png',(1920,1080))):
            asset=item[kind]
            expected=f'assets/marketing/{folder}/{item["platform"]}/{item["theme"]}/{item["screen"]}{suffix}'
            assert asset['src']==expected
            image_path=ROOT/'website/optimized'/asset['src'].removeprefix('assets/marketing/')
            assert image_path.is_file(),f'Gallery image is missing: {image_path}'
            with Image.open(image_path) as image:
                assert image.size==dimensions==(asset['width'],asset['height'])
tracked_generated={item['path'] for item in manifest['generated']}
for folder in ('composites','store/windows/screenshots','store/windows/icons','store/windows/promotional','store/android','website/optimized'):
    for path in (ROOT/folder).rglob('*') if (ROOT/folder).exists() else ():
        if path.suffix.lower() not in ('.png','.webp'):
            continue
        assert path.relative_to(ROOT).as_posix() in tracked_generated,f'Untracked generated image: {path}'
print(f'Validated {len(accounts)} fictional accounts, {len(icons)} unique icons, {len(files)} source captures. Manual legal and visual review remains required.')
