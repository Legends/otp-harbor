"""Generate deterministic derivatives only from reviewed genuine app captures."""
import argparse, hashlib, json, shutil, subprocess, sys
from pathlib import Path
from PIL import Image, ImageDraw, ImageFont
from screenshot_cleanup import clean_android, clean_desktop, DESKTOP_CORNER_RADIUS
ROOT=Path(__file__).resolve().parents[2]/'artifacts/marketing'
parser=argparse.ArgumentParser()
parser.add_argument('--platform',choices=('all','desktop','android'),default='all')
args=parser.parse_args()
source=ROOT/'source'; manifest_path=ROOT/'manifest.json'
manifest=json.loads(manifest_path.read_text(encoding='utf-8'))
previous_generated=manifest.get('generated',[])
selected=('desktop','android') if args.platform=='all' else (args.platform,)
def belongs_to_selected(entry):
    path=entry['path']
    return path=='website/gallery.json' or any(path==f'composites/{platform}/light-dark-account-list.png'
        or path.startswith(f'website/optimized/{platform}/')
        or (platform=='desktop' and path.startswith('store/windows/'))
        or (platform=='android' and path.startswith('store/android/'))
        for platform in selected)
generated=[entry for entry in previous_generated if not belongs_to_selected(entry)]
publishable_screens={'account-list','settings','account-context-menu','account-editor','search','appearance','backup','imports','icon-management','security','create-group','delete-swipe','edit-swipe'}
rejected_captures={('android','light','delete-swipe'),('android','light','edit-swipe')}
store_screens={'account-list','settings','account-context-menu','search','appearance','backup','imports','icon-management','security'}
def digest(path):
    if path.suffix.lower()=='.json':
        text=path.read_text(encoding='utf-8').replace('\r\n','\n').replace('\r','\n')
        return hashlib.sha256(text.encode('utf-8')).hexdigest()
    return hashlib.sha256(path.read_bytes()).hexdigest()
def record(path, kind, parents=(), platform=None):
    relative=path.relative_to(ROOT).as_posix()
    entry={'path':relative,'sha256':digest(path),'kind':kind}
    if platform: entry['platform']=platform
    if parents: entry['sourceSha256']=[digest(p) for p in parents]
    return entry
for platform in selected:
    for theme in ('light','dark'):
        folder=source/platform/theme
        images=sorted(folder.glob('*.png')) if folder.exists() else []
        if not images: raise SystemExit(f'Missing {platform} {theme} captures')
        for path in images:
            image=Image.open(path); image.verify()
            if path.stem not in publishable_screens or (platform,theme,path.stem) in rejected_captures:
                continue
            output_name=path.name
            destination=ROOT/'website/optimized'/platform/theme/output_name
            destination.parent.mkdir(parents=True,exist_ok=True)
            with Image.open(path) as image:
                cleaned,crop=clean_android(image) if platform=='android' else clean_desktop(image)
                cleaned.save(destination,optimize=True)
            web_entry=record(destination,'cleaned-website-capture',[path],platform)
            web_entry['sourcePath']=path.relative_to(ROOT).as_posix()
            web_entry['sourceCrop']=list(crop)
            if platform=='desktop': web_entry['cornerMaskRadius']=DESKTOP_CORNER_RADIUS
            generated.append(web_entry)
            if platform=='desktop' and path.stem in store_screens:
                with Image.open(destination) as image:
                    app_pixels=image.convert('RGBA')
                    width,height=app_pixels.size
                    genuine=False  # A cleaned, masked capture is a documented derivative.
                    if not genuine:
                        frame_width=max(1366,width+160)
                        frame_height=max(frame_width+34,height+120)
                        background='#e8eef7' if theme=='light' else '#071121'
                        framed=Image.new('RGB',(frame_width,frame_height),background)
                        position=((frame_width-width)//2,(frame_height-height)//2)
                        framed.paste(app_pixels,position,app_pixels)
                store=ROOT/'store/windows/screenshots'/theme/path.name
                store.parent.mkdir(parents=True,exist_ok=True)
                if genuine:
                    shutil.copyfile(path,store)
                else:
                    framed.save(store,optimize=True)
                if store.stat().st_size>50_000_000: raise SystemExit(f'Store image exceeds 50MB: {store}')
                entry=record(store,'genuine-store-screenshot' if genuine else 'framed-store-screenshot-candidate',[path],platform)
                if not genuine:
                    entry['nativeAppBounds']=[*position,width,height]
                    entry['sourceCrop']=list(crop)
                    entry['cornerMaskRadius']=DESKTOP_CORNER_RADIUS
                    entry['publicationUse']='Store screenshot candidate: cleaned app pixels on neutral frame; manual review required'
                generated.append(entry)
    left=source/platform/'light'/'account-list.png'
    right=source/platform/'dark'/'account-list.png'
    if not left.exists() or not right.exists():
        print(f'Skipping {platform} comparison until both account-list captures exist')
    else:
        with Image.open(left) as original_light, Image.open(right) as original_dark:
            light=clean_android(original_light)[0] if platform=='android' else clean_desktop(original_light)[0]
            dark=clean_android(original_dark)[0] if platform=='android' else clean_desktop(original_dark)[0]
            if light.size!=dark.size: raise SystemExit(f'{platform} Light/Dark account lists differ in size')
            gap=32; pad=48; label=60
            width=light.width*2+gap+pad*2; height=light.height+pad*2+label
            canvas=Image.new('RGB',(width,height),'#101C31')
            canvas.paste(light,(pad,pad+label),light if light.mode=='RGBA' else None)
            canvas.paste(dark,(pad+light.width+gap,pad+label),dark if dark.mode=='RGBA' else None)
            draw=ImageDraw.Draw(canvas)
            # Pillow's bundled default font is used; no external font assets.
            font=ImageFont.load_default(size=34)
            draw.text((pad,pad),'Light Theme',fill='#FFFFFF',font=font)
            draw.text((pad+light.width+gap,pad),'Dark Theme',fill='#FFFFFF',font=font)
            target=ROOT/'composites'/platform/'light-dark-account-list.png'
            target.parent.mkdir(parents=True,exist_ok=True)
            canvas.save(target,optimize=True)
            generated.append(record(target,'designed-comparison',[left,right],platform))
if 'desktop' in selected:
    app_icon=ROOT.parents[1]/'OtpHarbor.UI.Avalonia.Desktop/Assets/Icons/app-1024.png'
    with Image.open(app_icon) as original:
        if original.width<300 or original.height<300 or original.width!=original.height:
            raise SystemExit(f'App icon master must be square and at least 300 pixels: {app_icon}')
        tile=original.convert('RGBA').resize((300,300),Image.Resampling.LANCZOS)
    tile_path=ROOT/'store/windows/icons/otp-harbor-tile-300.png'
    tile_path.parent.mkdir(parents=True,exist_ok=True)
    tile.save(tile_path,optimize=True)
    entry=record(tile_path,'store-app-tile-icon',[app_icon],'desktop')
    entry['sourcePath']=app_icon.relative_to(ROOT.parents[1]).as_posix()
    generated.append(entry)

    # The published v1 Store gallery established this campaign layout. Keep v3
    # artwork separate from genuine captures and the plain gallery candidates.
    campaign_background=ROOT.parents[1]/'packaging/windows-store/assets/source/store-marketing-screenshot-background.png'
    campaign_dir=ROOT/'store/windows/promotional'
    campaign_dir.mkdir(parents=True,exist_ok=True)
    copy={
        'en-US':[
            ('Made for focus.\nBuilt for trust.','Keep your accounts in view with a comfortable dark theme.','DARK THEME'),
            ('One app.\nTwo looks.','Choose the theme that suits your space.','LIGHT + DARK'),
            ('Make it yours.','Switch themes and tailor how your codes appear.','APPEARANCE'),
            ('The next action\nis right there.','Edit an account or show its QR code from the account menu.','ACCOUNT ACTIONS'),
            ('Quick to unlock.','Windows Hello uses your device PIN or supported biometrics such as fingerprint. Your master password remains the recovery method.','WINDOWS HELLO'),
            ('Scan or import\na QR code.','Add accounts with your camera or a QR image. The example code opens the OTP Harbor website.','CAMERA OR FILE'),
            ('Make every account\neasy to find.','Use groups to keep related accounts together and mark the ones you use most as favourites.','GROUPS · FAVOURITES'),
            ('Make account icons\nyour own.','Download OTP Harbor Icon Pack Builder from GitHub, create a local pack of brand logos, then import it in Settings.','ICON PACKS'),
            ('Keep a way back.','Export a portable, password-protected backup. Restore your accounts when needed.','BACKUP + RESTORE'),
            ('Bring your codes along.','Add accounts manually, scan a QR code or import a saved QR image. Migration formats are listed alongside.','IMPORT FORMATS'),
        ],
        'de-DE':[
            ('Klarer Fokus.\nSicher im Alltag.','Behalte deine Konten auch im dunklen Design im Blick.','DUNKLES DESIGN'),
            ('Eine App.\nZwei Ansichten.','Wähle das Design, das zu dir passt.','HELL + DUNKEL'),
            ('Ganz nach\ndeinem Stil.','Wechsle das Design und passe die Codeanzeige an.','DARSTELLUNG'),
            ('Alles direkt\nzur Hand.','Bearbeite Konten oder zeige ihren QR-Code im Kontomenü.','KONTOAKTIONEN'),
            ('Schnell entsperrt.','Windows Hello nutzt die Geräte-PIN oder unterstützte Biometrie wie Fingerabdruck. Das Masterpasswort bleibt die Wiederherstellung.','WINDOWS HELLO'),
            ('QR-Code scannen\noder importieren.','Füge Konten per Kamera oder QR-Bild hinzu. Der Beispielcode öffnet die OTP Harbor-Website.','KAMERA ODER DATEI'),
            ('Jedes Konto\nschnell finden.','Ordne zusammengehörige Konten in Gruppen und markiere häufig genutzte Konten als Favoriten.','GRUPPEN · FAVORITEN'),
            ('Konto-Icons\nselbst gestalten.','Lade OTP Harbor Icon Pack Builder von GitHub herunter, erstelle lokal ein Paket mit Markenlogos und importiere es in den Einstellungen.','ICON-PACKS'),
            ('Sicher wiederherstellen.','Exportiere ein portables, passwortgeschütztes Backup. Stelle deine Konten bei Bedarf wieder her.','BACKUP + WIEDERHERSTELLEN'),
            ('Konten mitnehmen.','Füge Konten manuell hinzu, scanne einen QR-Code oder importiere ein gespeichertes QR-Bild.','IMPORTFORMATE'),
        ],
        'fr-FR':[
            ('Restez concentré.\nGardez le contrôle.','Retrouvez vos comptes dans un thème sombre confortable.','THÈME SOMBRE'),
            ('Une app.\nDeux styles.','Choisissez le thème qui vous convient.','CLAIR + SOMBRE'),
            ('À votre façon.','Changez de thème et ajustez l’affichage des codes.','APPARENCE'),
            ('Tout à portée\nde main.','Modifiez un compte ou affichez son code QR depuis le menu.','ACTIONS DU COMPTE'),
            ('Accès rapide.','Windows Hello utilise le code PIN ou une méthode biométrique compatible comme l’empreinte digitale. Le mot de passe principal reste la méthode de récupération.','WINDOWS HELLO'),
            ('Scannez ou importez\nun code QR.','Ajoutez des comptes avec la caméra ou une image QR. Le code exemple ouvre le site OTP Harbor.','CAMÉRA OU FICHIER'),
            ('Retrouvez vite\nvos comptes.','Regroupez les comptes associés et ajoutez vos comptes les plus utilisés aux favoris.','GROUPES · FAVORIS'),
            ('Des icônes pour\nvos comptes.','Téléchargez OTP Harbor Icon Pack Builder sur GitHub, créez localement un pack de logos de marques, puis importez-le dans les paramètres.','PACKS D’ICÔNES'),
            ('Gardez une issue.','Exportez une sauvegarde portable protégée par mot de passe. Restaurez vos comptes au besoin.','SAUVEGARDE + RESTAURATION'),
            ('Importez vos comptes.','Ajoutez-les manuellement, scannez un code QR ou importez une image QR enregistrée.','FORMATS D’IMPORT'),
        ],
        'es-ES':[
            ('Más claridad.\nMás control.','Consulta tus cuentas con un cómodo tema oscuro.','TEMA OSCURO'),
            ('Una app.\nDos estilos.','Elige el tema que mejor se adapte a ti.','CLARO + OSCURO'),
            ('A tu manera.','Cambia el tema y ajusta cómo se muestran los códigos.','APARIENCIA'),
            ('Todo a mano.','Edita una cuenta o muestra su código QR desde el menú.','ACCIONES DE CUENTA'),
            ('Desbloqueo rápido.','Windows Hello usa el PIN o un método biométrico compatible, como la huella digital. La contraseña maestra permite recuperar el acceso.','WINDOWS HELLO'),
            ('Escanea o importa\nun código QR.','Añade cuentas con la cámara o una imagen QR. El código de ejemplo abre el sitio de OTP Harbor.','CÁMARA O ARCHIVO'),
            ('Encuentra cada\ncuenta enseguida.','Agrupa las cuentas relacionadas y marca como favoritas las que más utilizas.','GRUPOS · FAVORITOS'),
            ('Iconos para\ntus cuentas.','Descarga OTP Harbor Icon Pack Builder de GitHub, crea un paquete local de logotipos de marcas e impórtalo desde Ajustes.','PAQUETES DE ICONOS'),
            ('Conserva una copia.','Exporta una copia portátil protegida con contraseña. Restaura tus cuentas cuando lo necesites.','COPIA + RESTAURACIÓN'),
            ('Trae tus cuentas.','Añádelas manualmente, escanea un código QR o importa una imagen QR guardada.','FORMATOS DE IMPORTACIÓN'),
        ],
    }
    campaign_screens=[
        ('02-dark-account-list.png',['source/desktop/dark/account-list.png'],'standard'),
        ('03-light-dark-themes.png',['source/desktop/light/account-list.png','source/desktop/dark/account-list.png'],'compare'),
        ('04-appearance-settings.png',['source/desktop/dark/settings.png'],'standard'),
        ('05-account-actions.png',['source/desktop/dark/account-context-menu.png'],'standard'),
        ('06-quick-unlock.png',['composites/desktop/biometric-prompt-dark-transparent.png'],'prompt'),
        ('07-qr-import.png',['composites/desktop/scanner-website-qr-dark.png'],'scanner'),
        ('08-groups-favourites.png',[],'organize-illustration'),
        ('09-icon-pack.png',[],'icon-pack-illustration'),
        ('10-backup-restore.png',[],'backup-illustration'),
        ('11-import-formats.png',[],'formats-illustration'),
    ]
    section_labels={
        'en-US':('GROUPS + FAVOURITES','ACCOUNT ICON PACKS','PORTABLE BACKUP + RESTORE','SUPPORTED IMPORT FORMATS'),
        'de-DE':('GRUPPEN + FAVORITEN','KONTO-ICON-PACKS','PORTABLES BACKUP + WIEDERHERSTELLEN','UNTERSTÜTZTE IMPORTFORMATE'),
        'fr-FR':('GROUPES + FAVORIS','PACKS D’ICÔNES','SAUVEGARDE PORTABLE + RESTAURATION','FORMATS D’IMPORT PRIS EN CHARGE'),
        'es-ES':('GRUPOS + FAVORITOS','PAQUETES DE ICONOS','COPIA PORTÁTIL + RESTAURACIÓN','FORMATOS DE IMPORTACIÓN COMPATIBLES'),
    }
    format_labels={
        'en-US':('Aegis JSON · unencrypted','2FAS .2fas · unencrypted','otpauth:// list · .txt','Google Authenticator · QR image'),
        'de-DE':('Aegis JSON · unverschlüsselt','2FAS .2fas · unverschlüsselt','otpauth://-Liste · .txt','Google Authenticator · QR-Bild'),
        'fr-FR':('Aegis JSON · non chiffré','2FAS .2fas · non chiffré','Liste otpauth:// · .txt','Google Authenticator · image QR'),
        'es-ES':('Aegis JSON · sin cifrar','2FAS .2fas · sin cifrar','Lista otpauth:// · .txt','Google Authenticator · imagen QR'),
    }
    onboarding_root=ROOT.parents[1]/'OtpHarbor.UI.Avalonia.Shared/Assets/Onboarding'
    illustration_sources={
        'organize-illustration':[onboarding_root/'organize-accounts.png'],
        'icon-pack-illustration':[onboarding_root/'icon-pack.png'],
        'backup-illustration':[onboarding_root/'move-accounts.png'],
        'formats-illustration':[onboarding_root/'add-account.png'],
    }
    font_regular=ImageFont.truetype('C:/Windows/Fonts/segoeui.ttf',32) if Path('C:/Windows/Fonts/segoeui.ttf').exists() else ImageFont.truetype('DejaVuSans.ttf',32)
    font_bold=ImageFont.truetype('C:/Windows/Fonts/segoeuib.ttf',69) if Path('C:/Windows/Fonts/segoeuib.ttf').exists() else ImageFont.truetype('DejaVuSans-Bold.ttf',69)
    font_small=ImageFont.truetype('C:/Windows/Fonts/segoeuib.ttf',22) if Path('C:/Windows/Fonts/segoeuib.ttf').exists() else ImageFont.truetype('DejaVuSans-Bold.ttf',22)
    font_disclaimer=ImageFont.truetype('C:/Windows/Fonts/segoeui.ttf',23) if Path('C:/Windows/Fonts/segoeui.ttf').exists() else ImageFont.truetype('DejaVuSans.ttf',23)
    icon_disclaimers={
        'en-US':'Third-party logos retain their own licenses, trademark rights and usage terms. You are responsible for compliant use.',
        'de-DE':'Für Logos Dritter gelten deren Lizenzen, Markenrechte und Nutzungsbedingungen. Du bist für die rechtmäßige Nutzung verantwortlich.',
        'fr-FR':'Les logos tiers restent soumis à leurs licences, droits de marque et conditions d’utilisation. Leur utilisation conforme vous incombe.',
        'es-ES':'Los logotipos de terceros conservan sus licencias, derechos de marca y condiciones de uso. Su uso conforme es tu responsabilidad.',
    }
    with Image.open(campaign_background) as original_background, Image.open(app_icon) as original_icon:
        background=original_background.convert('RGB').resize((1920,1080),Image.Resampling.LANCZOS)
        brand_icon=original_icon.convert('RGBA').resize((62,62),Image.Resampling.LANCZOS)
    shade=Image.new('RGBA',background.size)
    shade_pixels=shade.load()
    for x in range(1010):
        alpha=round(238*(1-x/1010)**0.6)
        for y in range(1080): shade_pixels[x,y]=(2,12,34,alpha)
    def paste_app(canvas, screenshot, bounds, crop_inset=0, source_crop=None):
        with Image.open(screenshot) as original:
            crop=source_crop or (crop_inset,crop_inset,original.width-crop_inset,original.height-crop_inset)
            if not (0<=crop[0]<crop[2]<=original.width and 0<=crop[1]<crop[3]<=original.height):
                raise ValueError(f'Invalid source crop for {screenshot}: {crop}')
            app=original.crop(crop).convert('RGB')
        x,y,max_width,max_height=bounds
        scale=min(max_width/app.width,max_height/app.height,1)
        size=(round(app.width*scale),round(app.height*scale))
        app=app.resize(size,Image.Resampling.LANCZOS)
        x+=(max_width-size[0])//2
        y+=(max_height-size[1])//2
        shadow=Image.new('RGBA',canvas.size)
        sd=ImageDraw.Draw(shadow)
        for radius,alpha in ((30,24),(22,30),(14,36)):
            sd.rounded_rectangle((x-radius//2,y-radius//2+12,x+size[0]+radius//2,y+size[1]+radius//2+12),radius=24,fill=(0,5,20,alpha))
        canvas.alpha_composite(shadow)
        corner_radius=min(22,size[0]//20,size[1]//20)
        mask=Image.new('L',size)
        ImageDraw.Draw(mask).rounded_rectangle((0,0,size[0]-1,size[1]-1),radius=corner_radius,fill=255)
        canvas.paste(app,(x,y),mask)
        return {'sourcePath':screenshot.relative_to(ROOT).as_posix(),'sourceCrop':list(crop),'appBounds':[x,y,*size],'cornerMaskRadius':corner_radius}
    def paste_illustration(canvas, illustration, bounds):
        with Image.open(illustration) as original:
            art=original.convert('RGB')
        x,y,max_width,max_height=bounds
        scale=min(max_width/art.width,max_height/art.height,1)
        size=(round(art.width*scale),round(art.height*scale))
        art=art.resize(size,Image.Resampling.LANCZOS)
        x+=(max_width-size[0])//2
        y+=(max_height-size[1])//2
        mask=Image.new('L',size)
        ImageDraw.Draw(mask).rounded_rectangle((0,0,size[0]-1,size[1]-1),radius=16,fill=255)
        canvas.paste(art,(x,y),mask)
        return {'sourcePath':illustration.relative_to(ROOT.parents[1]).as_posix(),'bounds':[x,y,*size]}
    def paste_cutout(canvas, cutout, bounds):
        with Image.open(cutout) as original:
            visual=original.convert('RGBA')
        x,y,max_width,max_height=bounds
        scale=min(max_width/visual.width,max_height/visual.height,1)
        size=(round(visual.width*scale),round(visual.height*scale))
        visual=visual.resize(size,Image.Resampling.LANCZOS)
        x+=(max_width-size[0])//2
        y+=(max_height-size[1])//2
        shadow=Image.new('RGBA',canvas.size)
        ImageDraw.Draw(shadow).rounded_rectangle((x+14,y+20,x+size[0]+14,y+size[1]+20),radius=25,fill=(0,5,20,58))
        canvas.alpha_composite(shadow)
        canvas.alpha_composite(visual,(x,y))
        return {'sourcePath':cutout.relative_to(ROOT).as_posix(),'bounds':[x,y,*size]}
    for culture,slides in copy.items():
        if len(slides)!=len(campaign_screens):
            raise ValueError(f'Incomplete Store campaign copy for {culture}')
        for (filename,screen_names,layout),(headline,body,chip) in zip(campaign_screens,slides):
            screen_paths=[ROOT/name for name in screen_names]
            art_paths=illustration_sources.get(layout,[])
            if not all(path.is_file() for path in screen_paths+art_paths):
                print(f'Missing campaign source for {filename}; skipping {culture}')
                continue
            canvas=background.convert('RGBA')
            canvas.alpha_composite(shade)
            draw=ImageDraw.Draw(canvas)
            draw.rounded_rectangle((91,70,161,140),radius=15,fill='#101e43')
            canvas.alpha_composite(brand_icon,(95,74))
            draw.text((180,88),'OTP HARBOR  /  WINDOWS',font=font_small,fill='#EAF3FF')
            placements=[]
            cutouts=[]
            illustrations=[]
            if layout=='compare':
                draw.multiline_text((100,186),headline,font=font_bold,fill='#FFFFFF',spacing=10)
                import textwrap
                draw.multiline_text((105,410),'\n'.join(textwrap.wrap(body,24)),font=font_regular,fill='#BCD7F5',spacing=10)
                for index,screen in enumerate(screen_paths):
                    placements.append(paste_app(canvas,screen,(610+index*620,95,570,900)))
            else:
                draw.multiline_text((100,205),headline,font=font_bold,fill='#FFFFFF',spacing=10)
                import textwrap
                wrapped='\n'.join(textwrap.wrap(body,42))
                body_y=520 if layout=='icon-pack-illustration' else 575
                if layout=='icon-pack-illustration' and draw.multiline_textbbox((105,body_y),wrapped,font=font_regular,spacing=10)[3]>720:
                    raise ValueError(f'Icon pack instructions overlap the rights notice for {culture}')
                draw.multiline_text((105,body_y),wrapped,font=font_regular,fill='#BDD5EF',spacing=10)
                if layout=='prompt':
                    cutouts.append(paste_cutout(canvas,screen_paths[0],(1000,115,820,850)))
                elif layout=='scanner':
                    placements.append(paste_app(canvas,screen_paths[0],(785,170,1070,805)))
                elif layout in ('organize-illustration','icon-pack-illustration'):
                    draw.text((1000,185),section_labels[culture][0 if layout=='organize-illustration' else 1],font=font_small,fill='#FFFFFF')
                    illustrations.append(paste_illustration(canvas,art_paths[0],(900,255,950,605)))
                    if layout=='icon-pack-illustration':
                        disclaimer='\n'.join(textwrap.wrap(icon_disclaimers[culture],58))
                        if draw.multiline_textbbox((105,735),disclaimer,font=font_disclaimer,spacing=6)[3]>850:
                            raise ValueError(f'Icon pack rights notice overlaps the feature chip for {culture}')
                        draw.multiline_text((105,735),disclaimer,font=font_disclaimer,fill='#BDD5EF',spacing=6)
                elif layout=='backup-illustration':
                    draw.text((1000,185),section_labels[culture][2],font=font_small,fill='#FFFFFF')
                    illustrations.append(paste_illustration(canvas,art_paths[0],(900,255,950,605)))
                elif layout=='formats-illustration':
                    draw.text((1000,140),section_labels[culture][3],font=font_small,fill='#FFFFFF')
                    illustrations.append(paste_illustration(canvas,art_paths[0],(955,185,900,500)))
                    for index,label in enumerate(format_labels[culture]):
                        col,row=index%2,index//2
                        x,y=970+col*440,705+row*105
                        draw.rounded_rectangle((x,y,x+420,y+83),radius=17,fill='#102B57',outline='#47C7F7',width=2)
                        draw.text((x+20,y+24),label,font=font_small,fill='#FFFFFF')
                else:
                    inset=2 if filename in ('04-appearance-settings.png','05-account-actions.png') else 0
                    placements.append(paste_app(canvas,screen_paths[0],(1030,78,800,925),inset))
            draw=ImageDraw.Draw(canvas)
            chip_box=draw.textbbox((0,0),chip,font=font_small)
            chip_width=chip_box[2]-chip_box[0]+48
            draw.rounded_rectangle((100,865,100+chip_width,921),radius=17,fill='#132E57',outline='#36BDF4',width=2)
            draw.text((124,881),chip,font=font_small,fill='#FFFFFF')
            destination=campaign_dir/culture/filename
            destination.parent.mkdir(parents=True,exist_ok=True)
            # Windows image preview and indexing can briefly hold the existing PNG open.
            # Render once, then replace it after the writer has closed the temporary file.
            temporary=destination.with_name(destination.stem+'.generating.png')
            canvas.convert('RGB').save(temporary,optimize=True)
            import os,time
            for attempt in range(5):
                try:
                    os.replace(temporary,destination)
                    break
                except OSError:
                    if attempt==4: raise
                    time.sleep(0.2*(attempt+1))
            parents=[campaign_background,app_icon]+screen_paths+art_paths
            entry=record(destination,'designed-store-campaign',parents,'desktop')
            entry['backgroundPath']=campaign_background.relative_to(ROOT.parents[1]).as_posix()
            entry['appCapturePlacements']=placements
            if illustrations:
                entry['visualKind']='conceptual-promotion'
                entry['illustrationPlacements']=illustrations
            if cutouts: entry['cutoutPlacements']=cutouts
            entry['publicationUse']='Branded Store campaign candidate; genuine captures and labeled composites only; manual and Partner Center review required'
            entry['locale']=culture
            generated.append(entry)
gallery=[]
gallery_sources=[]
optimized_paths={entry['path'] for entry in generated if entry['path'].startswith('website/optimized/')}
for platform in ('desktop','android'):
    for theme in ('light','dark'):
        for capture in sorted((source/platform/theme).glob('*.png')):
            if f'website/optimized/{platform}/{theme}/{capture.name}' not in optimized_paths:
                continue
            with Image.open(ROOT/'website/optimized'/platform/theme/capture.name) as image:
                width,height=image.size
            screen=capture.stem.replace('-',' ')
            gallery.append({
                'platform':platform,'theme':theme,'screen':capture.stem,
                'src':f'assets/marketing/{platform}/{theme}/{capture.name}',
                'width':width,'height':height,
                'alt':f'OTP Harbor {platform} {screen} in {theme} theme with fictional demo accounts',
                'caption':f'{screen.title()} · {platform.title()} · {theme.title()} theme',
            })
            gallery_sources.append(capture)
gallery_path=ROOT/'website/gallery.json'
gallery_path.parent.mkdir(parents=True,exist_ok=True)
gallery_path.write_text(json.dumps({'items':gallery},indent=2)+'\n',encoding='utf-8')
generated.append(record(gallery_path,'website-gallery-data',gallery_sources))
manifest['generated']=sorted(generated,key=lambda entry:entry['path'])
current_paths={entry['path'] for entry in generated}
for entry in previous_generated:
    if belongs_to_selected(entry) and entry['path'] not in current_paths:
        stale=ROOT/entry['path']
        if stale.is_file(): stale.unlink()
store_priority={
    'light/account-list.png':(1,'Main account list — Light theme'),
    'dark/account-list.png':(2,'Main account list — Dark theme'),
    'light/settings.png':(3,'Appearance settings — Light theme'),
    'dark/settings.png':(4,'Appearance settings — Dark theme'),
    'dark/account-context-menu.png':(5,'Account actions and QR options'),
    'dark/imports.png':(6,'Account import formats — Dark theme'),
    'light/imports.png':(7,'Account import formats — Light theme'),
    'light/search.png':(8,'Find an account'),
    'light/account-editor.png':(9,'Add and edit accounts'),
    'light/backup.png':(10,'Encrypted backup and restore'),
}
store_entries=[entry for entry in generated if entry['kind'] in ('genuine-store-screenshot','framed-store-screenshot-candidate')]
store_entries.sort(key=lambda entry:store_priority.get(
    entry['path'].removeprefix('store/windows/screenshots/'),(99,entry['path']))[0])
available_store_paths={entry['path'] for entry in store_entries}
manifest['storeRecommendedOrder']=[{
    'path':f'store/windows/screenshots/{relative}',
    'caption':caption,
    'status':'ready for manual review' if f'store/windows/screenshots/{relative}' in available_store_paths else 'awaiting eligible genuine capture',
} for relative,(_,caption) in sorted(store_priority.items(),key=lambda item:item[1][0])]
manifest['storeUploadOrder']=[{
    'path':entry['path'],
    'caption':store_priority.get(
        entry['path'].removeprefix('store/windows/screenshots/'),
        (99,'OTP Harbor for Windows'))[1]
    } for entry in store_entries[:10]]
campaign_priority=[
    ('02-dark-account-list.png','Windows account list — Dark theme'),
    ('03-light-dark-themes.png','Light and Dark theme comparison'),
    ('04-appearance-settings.png','Appearance settings'),
    ('05-account-actions.png','Account actions'),
    ('06-quick-unlock.png','Windows Hello quick unlock'),
    ('07-qr-import.png','QR scanning and image import'),
    ('08-groups-favourites.png','Groups and favourites'),
    ('09-icon-pack.png','Import a locally built icon pack'),
    ('10-backup-restore.png','Encrypted backup and restore'),
    ('11-import-formats.png','Supported account import formats'),
]
manifest['storeCampaignOrder']={culture:[{
    'path':f'store/windows/promotional/{culture}/{filename}',
    'caption':caption,
    'status':('branded composite candidate; manual and Partner Center review required'
              if (ROOT/f'store/windows/promotional/{culture}/{filename}').is_file()
              else 'awaiting genuine Dark settings capture'),
} for filename,caption in campaign_priority] for culture in ('en-US','de-DE','fr-FR','es-ES')}
manifest['publicationStatus']='review required: inspect every image, legal names and app state before publication'
manifest_path.write_text(json.dumps(manifest,indent=2)+'\n',encoding='utf-8')
print(f'Generated {len(generated)} derivatives from genuine app captures.')
subprocess.run([sys.executable, str(Path(__file__).with_name('Generate-WebsiteShowcase.py'))], check=True)
