"""Browser regression checks for the shared static gallery (no production dependencies).
Run: pip install -r scripts/validation/gallery-browser-requirements.txt
     python -m playwright install chromium
     python scripts/validation/test_gallery_browser.py --report <optional JSON path>
"""
import argparse
import functools
import importlib.util
import json
import os
from pathlib import Path
import shutil
import tempfile
import threading
from http.server import SimpleHTTPRequestHandler, ThreadingHTTPServer
from playwright.sync_api import sync_playwright

ROOT = Path(__file__).resolve().parents[2]
INSTRUMENT = r"""
window.galleryTest = {decode: [], errors: [], lcp: 0};
new PerformanceObserver(list => { for (const e of list.getEntries()) galleryTest.lcp = e.startTime; }).observe({type:'largest-contentful-paint', buffered:true});
const originalDecode = HTMLImageElement.prototype.decode;
HTMLImageElement.prototype.decode = async function() {
  const start = performance.now();
  await originalDecode.call(this);
  galleryTest.decode.push({src: this.src, ms: performance.now()-start});
};
window.addEventListener('error', e => galleryTest.errors.push(e.message));
"""


class QuietHandler(SimpleHTTPRequestHandler):
    def log_message(self, *_):
        pass


def assemble(target):
    shutil.copytree(ROOT / 'site', target, dirs_exist_ok=True)
    assets = target / 'assets'
    (assets / 'android').mkdir(parents=True)
    shutil.copytree(ROOT / 'artifacts/marketing/website/optimized', assets / 'marketing')
    shutil.copy2(ROOT / 'artifacts/marketing/website/gallery.json', assets / 'marketing/gallery.json')
    shutil.copy2(ROOT / 'OtpHarbor.UI.Avalonia.Desktop/Assets/Icons/app-1024.png', assets / 'app-icon.png')
    shutil.copy2(ROOT / 'packaging/windows-store/screenshots/en-US/marketing/02-dark-account-list.png', assets / 'otp-harbor-totp-authenticator-windows.png')
    for image in (ROOT / 'packaging/android/marketing/en-US').glob('*.png'):
        shutil.copy2(image, assets / 'android' / image.name)
    spec = importlib.util.spec_from_file_location('prepare_gallery', ROOT / 'scripts/marketing/Prepare-PublicGallery.py')
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    original = (target / 'index.html').read_bytes()
    assert not module.prepare(target, {'publicationStatus': 'review required'})
    assert original == (target / 'index.html').read_bytes(), 'Unapproved gallery changed HTML'
    # An isolated fixture exercises the approved path without changing real approval metadata.
    assert module.prepare(target, {'publicationStatus': 'approved for website publication'})
    html = (target / 'index.html').read_text(encoding='utf-8')
    assert 'content="https://legends.github.io/otp-harbor/assets/otp-harbor-totp-authenticator-windows.png"' in html
    assert 'id="android-campaign-legacy"' not in (target / 'android/index.html').read_text(encoding='utf-8')


def new_page(browser, width=1440, policy=None, idle=True):
    page = browser.new_page(viewport={'width': width, 'height': 1000}, reduced_motion='reduce')
    page.add_init_script(INSTRUMENT)
    if policy:
        page.add_init_script('Object.defineProperty(navigator,"connection",{value:' + json.dumps(policy) + '});')
    if not idle:
        page.add_init_script('delete window.requestIdleCallback;')
    return page


def loaded(page, url, grid='#showcase-grid'):
    page.goto(url, wait_until='load')
    page.wait_for_selector(grid + ' button')


def ready(page, grid, key):
    page.wait_for_function('([s,k]) => { const g=document.querySelector(s); return g.dataset.gallery===k && g.getAttribute("aria-busy")!=="true" && [...g.querySelectorAll("img")].every(i=>i.complete&&i.naturalWidth); }', arg=[grid, key])


def click(page, selector):
    # DOM activation excludes Playwright's automatic scrolling/actionability wait from switch timing.
    page.locator(selector).evaluate('(button) => button.click()')


def wait_for_routes(page, routes, count):
    for _ in range(100):
        if len(routes) >= count:
            return
        page.wait_for_timeout(50)
    raise AssertionError(f'Expected {count} intercepted image requests, got {len(routes)}')


def desktop(page, url, report):
    requests = []
    page.on('request', lambda req: requests.append(req.url))
    loaded(page, url)
    # All previews, including initially lazy bottom cards, prepare after critical load.
    page.wait_for_function('galleryTest.decode.filter(e=>e.src.includes("/previews/")).length>=18')
    page.locator('#showcase').scroll_into_view_if_needed()
    ready(page, '#showcase-grid', 'desktop/dark')
    page.wait_for_function('document.querySelectorAll("#showcase-grid .preview-loading").length===0')
    page.evaluate('window.originalCards=[...document.querySelectorAll("#showcase-grid button")]')
    before = len(requests)
    before_decode = page.evaluate('galleryTest.decode.length')
    paint_timings = []
    for _ in range(3):
        for selector, key in [('[data-gallery-theme="light"]', 'desktop/light'), ('[data-gallery-platform="android"]', 'android/light'), ('[data-gallery-theme="dark"]', 'android/dark'), ('[data-gallery-platform="desktop"]', 'desktop/dark')]:
            paint_timings.append(page.locator(selector).evaluate('''async button => {
                const started=performance.now(); button.click();
                await new Promise(requestAnimationFrame); await new Promise(requestAnimationFrame);
                return performance.now()-started;
            }'''))
            ready(page, '#showcase-grid', key)
            assert page.locator('#showcase-grid button').count() == (5 if key.startswith('desktop/') else 4), key
    assert len(requests) == before, 'Prepared switches caused new network requests'
    assert page.evaluate('galleryTest.decode.length') == before_decode, 'Prepared switches decoded again'
    assert page.evaluate('originalCards.every((c,i)=>c===document.querySelectorAll("#showcase-grid button")[i])'), 'Cards were recreated'
    timings = page.evaluate('performance.getEntriesByName("gallery-switch").map(e=>e.duration)')
    assert max(timings) < 100, timings
    assert max(paint_timings) < 100, paint_timings
    assert not any('/zoom/' in request for request in requests), 'Full-size gallery image requested at startup'
    assert not any('/assets/otp-harbor-totp-authenticator-windows.png' in request for request in requests), 'Fallback hero downloaded'
    assert len([request for request in requests if '/previews/desktop/dark/account-list.webp' in request]) == 1, 'Hero downloaded twice'
    assert page.evaluate('''() => {
        const loaded=performance.getEntriesByType('navigation')[0].loadEventEnd;
        return performance.getEntriesByType('resource').filter(e=>e.name.includes('/previews/')&&!e.name.includes('/desktop/dark/')).every(e=>e.startTime>=loaded);
    }'''), 'Speculative preloads started during critical loading'
    report['desktop'] = page.evaluate('({lcp: galleryTest.lcp, load:performance.getEntriesByType("navigation")[0].loadEventEnd, images:performance.getEntriesByType("resource").filter(e=>/\\.(png|webp)$/.test(e.name)).map(e=>({url:new URL(e.name).pathname,bytes:e.transferSize,duration:e.duration})), decode:galleryTest.decode, switch_ms:performance.getEntriesByName("gallery-switch").map(e=>e.duration)})')
    report['desktop']['paint_switch_ms'] = paint_timings
    page.screenshot(path=str(ROOT / '.codex/gallery-perf/desktop-after.png')) if (ROOT / '.codex/gallery-perf').exists() else None
    # Hold the full-size response: only a loading message may appear before decode finishes.
    held = []
    page.route('**/zoom/desktop/dark/account-list.png', lambda route: held.append(route))
    first = page.locator('#showcase-grid button').first
    first.focus()
    page.keyboard.press('Enter')
    page.wait_for_function('document.querySelector("dialog").open')
    wait_for_routes(page, held, 1)
    assert held and page.locator('dialog img').is_hidden()
    assert page.get_by_text('Loading full-size image…', exact=True).is_visible()
    held.pop().continue_()
    page.wait_for_function('document.querySelector("dialog img").naturalWidth>=1920 && !document.querySelector("dialog img").hidden')
    assert page.locator('dialog .showcase-close').evaluate('(e)=>e===document.activeElement')
    page.keyboard.press('Escape')
    assert not page.locator('dialog').evaluate('(d)=>d.open')
    assert first.evaluate('(e)=>e===document.activeElement'), 'Dialog did not restore focus'
    click(page, '[data-gallery-theme="light"]')
    ready(page, '#showcase-grid', 'desktop/light')
    light_menu = page.locator('#showcase-grid button').filter(has_text='Account Context Menu')
    assert light_menu.count() == 1
    assert light_menu.locator('img').get_attribute('src').endswith('/previews/desktop/light/account-context-menu.webp')
    light_menu.click()
    page.wait_for_function('''() => {
        const image = document.querySelector('dialog img');
        return !image.hidden && image.naturalWidth >= 1920
            && image.src.endsWith('/zoom/desktop/light/account-context-menu.png');
    }''')
    page.keyboard.press('Escape')
    assert not page.evaluate('galleryTest.errors'), page.evaluate('galleryTest.errors')
    report['desktop']['checks'] = '4 groups, 12 cached switches, no requests/redecode, reused DOM, keyboard/full-size dialog, no duplicate hero'


def constrained(browser, url, report):
    results = []
    for width, connection, budget, idle in [(360, {'saveData': True, 'effectiveType': '4g'}, 0, True), (430, {'saveData': False, 'effectiveType': '2g'}, 0, True), (390, {'saveData': False, 'effectiveType': '3g'}, 2, True), (430, {'saveData': False, 'effectiveType': '4g'}, 4, True), (600, {'saveData': False, 'effectiveType': '4g'}, 4, False)]:
        page = new_page(browser, width, connection, idle)
        requests = []
        page.on('request', lambda req: requests.append(req.url))
        loaded(page, url)
        page.wait_for_timeout(1200)
        alternate = [request for request in requests if '/previews/' in request and '/desktop/dark/' not in request]
        assert len(alternate) <= budget, (width, alternate)
        assert page.evaluate('document.documentElement.scrollWidth<=innerWidth'), f'Overflow at {width}'
        click(page, '[data-gallery-theme="light"]')
        ready(page, '#showcase-grid', 'desktop/light')
        page.locator('#showcase').scroll_into_view_if_needed()
        assert page.locator('#showcase-grid .preview-loading').count() == 0
        assert page.evaluate('document.documentElement.scrollWidth<=innerWidth')
        results.append({'width': width, 'budget': budget, 'alternate_requests': len(alternate), 'idle_api': idle})
        page.close()
    report['constrained'] = results


def races_and_failures(browser, url, report):
    page = new_page(browser, policy={'saveData': True})
    held = []
    page.route('**/previews/desktop/light/*.webp', lambda route: held.append(route))
    loaded(page, url)
    page.locator('#showcase').scroll_into_view_if_needed()
    click(page, '[data-gallery-theme="dark"]')
    ready(page, '#showcase-grid', 'desktop/dark')
    click(page, '[data-gallery-theme="light"]')
    page.wait_for_timeout(100)
    click(page, '[data-gallery-platform="android"]')
    ready(page, '#showcase-grid', 'android/light')
    for route in held:
        try: route.continue_()
        except Exception: pass  # An intentionally preempted speculative request may be canceled.
    page.wait_for_timeout(100)
    assert page.locator('#showcase-grid').get_attribute('data-gallery') == 'android/light'
    page.route('**/previews/android/dark/settings.webp', lambda route: route.abort())
    click(page, '[data-gallery-theme="dark"]')
    page.get_by_role('button', name='Retry gallery', exact=True).wait_for(state='visible')
    assert page.locator('#showcase-grid').get_attribute('data-gallery') == 'android/light'
    page.unroute('**/previews/android/dark/settings.webp')
    page.get_by_role('button', name='Retry gallery', exact=True).click()
    ready(page, '#showcase-grid', 'android/dark')
    page.route('**/zoom/android/dark/account-list.png', lambda route: route.abort())
    page.locator('#showcase-grid button').first.click()
    page.get_by_role('button', name='Retry full-size image', exact=True).wait_for(state='visible')
    assert page.locator('dialog img').is_hidden()
    page.unroute('**/zoom/android/dark/account-list.png')
    page.get_by_role('button', name='Retry full-size image', exact=True).click()
    page.wait_for_function('document.querySelector("dialog img").naturalWidth>=1920 && !document.querySelector("dialog img").hidden')
    page.keyboard.press('Escape')
    assert not page.evaluate('galleryTest.errors')
    page.close()
    report['races_and_failures'] = 'Latest selection wins; old gallery retained on error; gallery/full-size retry recover'


def visibility_and_priority(browser, url, report):
    page = new_page(browser)
    page.add_init_script('window.testHidden=true; Object.defineProperty(document,"hidden",{get:()=>window.testHidden});')
    held = []
    requests = []
    page.on('request', lambda request: requests.append(request.url))
    page.route('**/previews/desktop/light/*.webp', lambda route: held.append(route))
    loaded(page, url)
    page.wait_for_timeout(500)
    assert not any('/desktop/light/' in req or '/android/' in req for req in requests), 'Hidden page preloaded'
    page.evaluate('window.testHidden=false; document.dispatchEvent(new Event("visibilitychange"))')
    wait_for_routes(page, held, 1)
    assert held
    # One held Desktop Light image must not block the other worker from preparing Android.
    page.wait_for_function('galleryTest.decode.some(e=>e.src.includes("/android/dark/"))')
    click(page, '[data-gallery-platform="android"]')
    ready(page, '#showcase-grid', 'android/dark')
    for route in held:
        try: route.continue_()
        except Exception: pass
    page.close()
    report['visibility_and_priority'] = 'Hidden queue paused; round-robin worker advances; user selection preempts stalled background'


def android_page(browser, url, report):
    for width in (360, 430, 820, 1440):
        page = new_page(browser, width)
        requests = []
        page.on('request', lambda request: requests.append(request.url))
        loaded(page, url + 'android/', '#android-showcase-grid')
        page.locator('#android-showcase').scroll_into_view_if_needed()
        click(page, '[data-android-theme="light"]')
        ready(page, '#android-showcase-grid', 'android/light')
        click(page, '[data-android-theme="dark"]')
        ready(page, '#android-showcase-grid', 'android/dark')
        assert page.evaluate('document.documentElement.scrollWidth<=innerWidth'), f'Android overflow {width}'
        assert not any('/assets/android/' in req or '/zoom/' in req for req in requests)
        assert not page.evaluate('galleryTest.errors')
        page.close()
    report['android_page'] = 'Light/Dark and responsive layouts at 360, 430, 820, 1440px; no legacy full-size downloads'


def queue_and_fallback(browser, url, report):
    page = new_page(browser)
    held = []
    draining = False

    def hold_alternate_preview(route):
        if draining or '/desktop/dark/' in route.request.url:
            route.continue_()
        else:
            held.append(route)

    page.route('**/previews/**/*.webp', hold_alternate_preview)
    loaded(page, url)
    wait_for_routes(page, held, 2)
    assert len(held) == 2, 'Background concurrency exceeded two'
    assert '/desktop/light/' in held[0].request.url and '/android/dark/' in held[1].request.url
    held.pop(1).continue_()
    wait_for_routes(page, held, 2)
    assert len(held) == 2 and '/android/light/' in held[1].request.url, 'One group blocked the other groups'
    # Drain pending routes before closing or removing interception. New requests
    # must proceed during cleanup instead of being added to the held list.
    draining = True
    for route in held:
        route.continue_()
    page.wait_for_load_state('networkidle')
    page.close()
    # Browsers without decode() must still wait for a successful load.
    page = new_page(browser, policy={'saveData': True}, idle=False)
    page.add_init_script('HTMLImageElement.prototype.decode=undefined;')
    loaded(page, url)
    click(page, '[data-gallery-theme="light"]')
    ready(page, '#showcase-grid', 'desktop/light')
    assert page.locator('#showcase-grid .preview-loading').count() == 0
    page.close()
    report['queue_and_fallback'] = 'Two speculative slots; Desktop Light/Android Dark first; fair progress; load-event decode fallback'


def decode_barrier(browser, url, report):
    page = new_page(browser, policy={'saveData': True})
    page.add_init_script('''const realDecode=HTMLImageElement.prototype.decode;
        HTMLImageElement.prototype.decode=async function(){
          await realDecode.call(this);
          if(this.src.includes('/desktop/light/settings.webp')) await new Promise(r=>window.releaseDecode=r);
        };''')
    loaded(page, url)
    click(page, '[data-gallery-theme="light"]')
    page.wait_for_function('typeof releaseDecode==="function"')
    assert page.locator('#showcase-grid').get_attribute('data-gallery') == 'desktop/dark', 'Gallery displayed before decode completed'
    page.evaluate('releaseDecode()')
    ready(page, '#showcase-grid', 'desktop/light')
    # A close while a full-size request is pending must not reveal a stale dialog later.
    held = []
    page.route('**/zoom/desktop/light/account-list.png', lambda route: held.append(route))
    page.locator('#showcase-grid button').first.click()
    page.wait_for_timeout(100)
    page.keyboard.press('Escape')
    assert held
    held.pop().continue_()
    page.wait_for_timeout(100)
    assert not page.locator('dialog').evaluate('(d)=>d.open')
    page.close()
    report['decode_barrier'] = 'Successful download cannot reveal a group before decode; late full-size completion cannot reopen dialog'


def lightbox(browser, url, report):
    def image_ready(page, suffix):
        page.wait_for_function('suffix => { const i=document.querySelector("dialog img"); return !i.hidden && i.naturalWidth>=1920 && i.src.endsWith(suffix); }', arg=suffix)

    for path, grid, platform, count in [('', '#showcase-grid', 'desktop', 5), ('android/', '#android-showcase-grid', 'android', 4)]:
        page = new_page(browser, width=360)
        page.set_viewport_size({'width': 360, 'height': 480})
        loaded(page, url + path, grid)
        click(page, '[data-android-theme="dark"]' if path else '[data-gallery-theme="dark"]')
        ready(page, grid, platform + '/dark')
        first = page.locator(grid + ' button').first
        assert not page.locator('dialog').evaluate('(d)=>d.open')
        first.locator('img').click()
        image_ready(page, f'/zoom/{platform}/dark/account-list.png')
        assert page.locator('dialog img').evaluate('(i)=>i.getAnimations().length===0'), 'Reduced-motion opening animated'
        assert page.locator('.showcase-counter').inner_text() == f'Image 1 of {count}'
        page.keyboard.press('ArrowLeft')
        last = 'qr-scanning' if platform == 'desktop' else 'backup-restore'
        image_ready(page, f'/zoom/{platform}/dark/{last}.png')
        assert page.locator('.showcase-counter').inner_text() == f'Image {count} of {count}'
        page.keyboard.press('ArrowRight')
        image_ready(page, f'/zoom/{platform}/dark/account-list.png')
        next_button = page.get_by_role('button', name='Next image', exact=True)
        next_button.click()
        second = 'account-context-menu' if platform == 'desktop' else 'settings'
        image_ready(page, f'/zoom/{platform}/dark/{second}.png')
        assert next_button.evaluate('(e)=>e===document.activeElement'), 'Navigation moved keyboard focus'
        # Native modal focus stays inside while Tab/Shift+Tab cycle through controls.
        for _ in range(5):
            page.keyboard.press('Tab')
            assert page.evaluate('document.querySelector("dialog").contains(document.activeElement)')
        previous = page.get_by_role('button', name='Previous image', exact=True)
        previous.click()
        image_ready(page, f'/zoom/{platform}/dark/account-list.png')
        stage = page.locator('.showcase-stage')
        stage.dispatch_event('pointerdown', {'pointerType': 'touch', 'isPrimary': True, 'pointerId': 1, 'clientX': 250, 'clientY': 120})
        stage.dispatch_event('pointerup', {'pointerType': 'touch', 'isPrimary': True, 'pointerId': 1, 'clientX': 140, 'clientY': 125})
        image_ready(page, f'/zoom/{platform}/dark/{second}.png')
        assert page.locator('.showcase-counter').inner_text() == f'Image 2 of {count}'
        stage.dispatch_event('pointerdown', {'pointerType': 'touch', 'isPrimary': True, 'pointerId': 2, 'clientX': 250, 'clientY': 120})
        stage.dispatch_event('pointerup', {'pointerType': 'touch', 'isPrimary': True, 'pointerId': 2, 'clientX': 240, 'clientY': 230})
        assert page.locator('.showcase-counter').inner_text() == f'Image 2 of {count}', 'Vertical scroll navigated the gallery'
        assert page.evaluate('''() => {
            const d=document.querySelector('dialog'), r=d.getBoundingClientRect();
            return d.scrollWidth<=d.clientWidth && [...d.querySelectorAll('.showcase-navigation button,.showcase-close')].every(b=>{
                const q=b.getBoundingClientRect(); return q.width>=44&&q.height>=44&&q.top>=r.top&&q.bottom<=r.bottom;
            });
        }'''), 'Compact lightbox clips controls or shrinks touch targets'
        for width, height in ((430, 320), (900, 480)):
            page.set_viewport_size({'width': width, 'height': height})
            assert page.evaluate('''() => {
                const d=document.querySelector('dialog'), r=d.getBoundingClientRect();
                return d.scrollWidth<=d.clientWidth && [...d.querySelectorAll('.showcase-navigation button,.showcase-close')].every(b=>{
                    const q=b.getBoundingClientRect(); return q.width>=44&&q.height>=44&&q.top>=r.top&&q.bottom<=r.bottom;
                });
            }'''), (width, height, 'Lightbox navigation clipped')
        page.keyboard.press('Escape')
        assert first.evaluate('(e)=>e===document.activeElement')
        # Keyboard navigation must not survive the closed modal.
        page.keyboard.press('ArrowRight')
        assert not page.locator('dialog').evaluate('(d)=>d.open')
        click(page, '[data-android-theme="light"]' if path else '[data-gallery-theme="light"]')
        ready(page, grid, platform + '/light')
        light_first = page.locator(grid + ' button').first
        light_first.locator('img').click()
        image_ready(page, f'/zoom/{platform}/light/account-list.png')
        page.keyboard.press('ArrowRight')
        image_ready(page, f'/zoom/{platform}/light/{second}.png')
        assert page.locator('.showcase-counter').inner_text() == f'Image 2 of {count}'
        page.keyboard.press('Escape')
        assert light_first.evaluate('(e)=>e===document.activeElement')
        page.close()

    page = new_page(browser)
    loaded(page, url)
    ready(page, '#showcase-grid', 'desktop/dark')
    page.evaluate('''() => {
        const decode=HTMLImageElement.prototype.decode;
        HTMLImageElement.prototype.decode=async function() {
            await decode.call(this);
            if(this.src.endsWith('/zoom/desktop/dark/account-context-menu.png')) await new Promise(r=>window.releaseLightboxMenu=r);
            if(this.src.endsWith('/zoom/desktop/dark/settings.png')) await new Promise(r=>window.releaseLightboxSettings=r);
        };
    }''')
    page.locator('#showcase-grid button').first.click()
    image_ready(page, '/zoom/desktop/dark/account-list.png')
    page.keyboard.press('ArrowRight')
    page.wait_for_function('typeof releaseLightboxMenu==="function"')
    page.keyboard.press('ArrowRight')
    page.wait_for_function('typeof releaseLightboxSettings==="function"')
    page.keyboard.press('ArrowRight')
    image_ready(page, '/zoom/desktop/dark/data-backup.png')
    page.evaluate('releaseLightboxMenu(); releaseLightboxSettings()')
    image_ready(page, '/zoom/desktop/dark/data-backup.png')
    assert page.locator('.showcase-counter').inner_text() == 'Image 4 of 5'
    page.keyboard.press('Escape')
    page.close()

    page = new_page(browser)
    loaded(page, url)
    ready(page, '#showcase-grid', 'desktop/dark')
    page.route('**/zoom/desktop/dark/account-context-menu.png', lambda route: route.abort())
    page.locator('#showcase-grid button').first.click()
    image_ready(page, '/zoom/desktop/dark/account-list.png')
    page.keyboard.press('ArrowRight')
    page.get_by_role('button', name='Retry full-size image', exact=True).wait_for(state='visible')
    page.keyboard.press('ArrowRight')
    image_ready(page, '/zoom/desktop/dark/settings.png')
    page.keyboard.press('ArrowLeft')
    page.get_by_role('button', name='Retry full-size image', exact=True).wait_for(state='visible')
    page.unroute('**/zoom/desktop/dark/account-context-menu.png')
    page.get_by_role('button', name='Retry full-size image', exact=True).click()
    image_ready(page, '/zoom/desktop/dark/account-context-menu.png')
    assert page.locator('.showcase-counter').inner_text() == 'Image 2 of 5'
    page.close()
    report['lightbox'] = 'Previous/next buttons, keyboard wraparound, group scope, modal focus/return, touch swipe/scroll, compact bounds, superseded decodes, failed-image navigation/retry'


def lightbox_motion(browser, url, report):
    for path, grid, platform, second in [('', '#showcase-grid', 'desktop', 'account-context-menu'), ('android/', '#android-showcase-grid', 'android', 'settings')]:
        page = new_page(browser)
        page.emulate_media(reduced_motion='no-preference')
        # Pause real browser animations so intermediate-frame assertions never depend on timing.
        page.add_init_script('''
            window.lightboxTransitions=[];
            const animate=HTMLImageElement.prototype.animate;
            HTMLImageElement.prototype.animate=function(...args) {
                const player=animate.apply(this,args); player.pause();
                lightboxTransitions.push(player); return player;
            };
        ''')
        loaded(page, url + path, grid)
        click(page, '[data-android-theme="dark"]' if path else '[data-gallery-theme="dark"]')
        ready(page, grid, platform + '/dark')
        first = page.locator(grid + ' button').first
        first.locator('img').click()
        page.wait_for_function('lightboxTransitions.length===1')
        assert page.evaluate('''() => {
            const player=lightboxTransitions[0], image=document.querySelector('dialog img');
            player.currentTime=90;
            const opacity=Number(getComputedStyle(image).opacity);
            return player.effect.target===image && player.effect.getTiming().duration===180 && opacity>0 && opacity<1;
        }'''), 'Opening does not fade a decoded image'

        page.evaluate('''suffix => {
            const decode=HTMLImageElement.prototype.decode; let held=false;
            HTMLImageElement.prototype.decode=async function() {
                await decode.call(this);
                if(!held && this.src.endsWith(suffix)) { held=true; await new Promise(r=>window.releaseMotionDecode=r); }
            };
        }''', f'/zoom/{platform}/dark/{second}.png')
        page.keyboard.press('ArrowRight')
        page.wait_for_function('typeof releaseMotionDecode==="function"')
        assert page.evaluate('lightboxTransitions.length===1 && lightboxTransitions[0].playState==="idle"')
        assert page.locator('dialog img').is_hidden(), 'Image appeared before decoding'
        page.set_viewport_size({'width': 430, 'height': 320})
        assert page.locator('.showcase-close').evaluate('(b)=>{const r=b.getBoundingClientRect();return r.top>=0&&r.right<=innerWidth&&r.bottom<=innerHeight;}'), 'Loading controls escaped the viewport after resizing'
        page.evaluate('releaseMotionDecode()')
        page.wait_for_function('lightboxTransitions.length===2')
        assert page.evaluate('''() => {
            const player=lightboxTransitions.at(-1), image=document.querySelector('dialog img');
            player.currentTime=90;
            const style=getComputedStyle(image);
            return Number(style.opacity)>0 && Number(style.opacity)<1 && new DOMMatrixReadOnly(style.transform).m41>0
                && getComputedStyle(document.querySelector('.showcase-stage')).overflowX==='hidden';
        }'''), 'Next image does not fade/slide from the right without overflow'

        page.keyboard.press('ArrowLeft')
        page.wait_for_function('lightboxTransitions.length===3')
        assert page.evaluate('''() => {
            const player=lightboxTransitions.at(-1); player.currentTime=90;
            return lightboxTransitions[1].playState==='idle' && new DOMMatrixReadOnly(getComputedStyle(player.effect.target).transform).m41<0;
        }'''), 'Previous image does not slide from the left or cancel its predecessor'
        page.evaluate('lightboxTransitions.at(-1).finish()')
        assert page.locator('dialog img').evaluate('(i)=>getComputedStyle(i).opacity==="1" && getComputedStyle(i).transform==="none"')

        page.keyboard.press('ArrowRight')
        page.wait_for_function('lightboxTransitions.length===4')
        page.emulate_media(reduced_motion='reduce')
        page.wait_for_function('lightboxTransitions.at(-1).playState==="idle"')
        page.keyboard.press('ArrowLeft')
        page.wait_for_function('document.querySelector("dialog img").src.endsWith("/account-list.png") && !document.querySelector("dialog img").hidden')
        assert page.evaluate('lightboxTransitions.length===4'), 'Reduced-motion navigation animated'
        assert page.locator('dialog img').evaluate('(i)=>i.getAnimations().length===0 && getComputedStyle(i).opacity==="1"')

        page.emulate_media(reduced_motion='no-preference')
        page.keyboard.press('ArrowRight')
        page.wait_for_function('lightboxTransitions.length===5')
        page.keyboard.press('Escape')
        page.wait_for_function('lightboxTransitions.at(-1).playState==="idle"')
        assert not page.locator('dialog').evaluate('(d)=>d.open')
        assert first.evaluate('(e)=>e===document.activeElement')
        assert not page.evaluate('galleryTest.errors')
        page.close()
    report['lightbox_motion'] = 'Real 180ms fade/directional slide, decode barrier, intermediate/final frames, predecessor/close cancellation, live reduced-motion changes; main and Android pages'


def lightbox_zoom(browser, url, report):
    for path, grid, platform, width, height in [('', '#showcase-grid', 'desktop', 1440, 1000), ('', '#showcase-grid', 'desktop', 360, 640), ('android/', '#android-showcase-grid', 'android', 430, 640)]:
        page = new_page(browser, width=width)
        page.set_viewport_size({'width': width, 'height': height})
        full_requests = []
        page.on('request', lambda request: full_requests.append(request.url) if '/zoom/' in request.url else None)
        loaded(page, url + path, grid)
        click(page, '[data-android-theme="dark"]' if path else '[data-gallery-theme="dark"]')
        ready(page, grid, platform + '/dark')
        opener = page.locator(grid + ' button').filter(has_text='Data & Backup') if platform == 'desktop' else page.locator(grid + ' button').first
        opener.locator('img').click()
        page.wait_for_function('document.querySelector(".showcase-stage").getAttribute("aria-busy")==="false"')
        image = page.locator('dialog img')
        assert '/zoom/' in image.get_attribute('src'), 'Viewer replaced the marketing frame'
        assert page.evaluate('''() => {
            const image=document.querySelector('dialog img').getBoundingClientRect(), bar=document.querySelector('.showcase-footer'), r=bar.getBoundingClientRect();
            const idealWidth=Math.min(innerWidth*.98, innerHeight*.98*16/9);
            const alpha=Number(getComputedStyle(bar).backgroundColor.match(/[0-9.]+/g).at(-1));
            return image.width>=idealWidth*.98 && r.bottom<=image.bottom+1 && r.top>image.top+image.height/2 && alpha>0 && alpha<1;
        }'''), 'Marketing frame does not fill the available space with a translucent bottom overlay'
        assert page.evaluate('''() => {
            const r=document.querySelector('dialog').getBoundingClientRect();
            return [...document.querySelectorAll('.showcase-tools button,.showcase-navigation button')].every(b=>{
                const p=b.getBoundingClientRect();return p.width>=44&&p.height>=44&&p.left>=r.left&&p.right<=r.right&&p.top>=r.top&&p.bottom<=r.bottom;
            });
        }'''), (width, height, 'Viewer controls are clipped')
        before = image.bounding_box()['width']
        position = page.locator('.showcase-counter').inner_text()
        requests = len(full_requests)
        page.get_by_role('button', name='Zoom in', exact=True).click()
        assert image.bounding_box()['width'] >= before * 1.45
        viewport = page.locator('.showcase-image-viewport')
        assert viewport.evaluate('(v)=>v.scrollWidth>v.clientWidth && (getComputedStyle(v).touchAction.includes("pan-x") || getComputedStyle(v).touchAction==="manipulation")')
        scroll = viewport.evaluate('(v)=>v.scrollLeft')
        bounds = viewport.bounding_box()
        x, y = bounds['x'] + bounds['width'] / 2, bounds['y'] + bounds['height'] / 2
        page.mouse.move(x, y)
        page.mouse.down()
        page.mouse.move(x + 60, y + 5, steps=4)
        assert viewport.evaluate('(v)=>getComputedStyle(v).cursor==="grabbing"')
        page.mouse.up()
        assert viewport.evaluate('(v)=>v.scrollLeft') < scroll - 40, 'Dragging did not pan the enlarged image'
        assert page.locator('.showcase-counter').inner_text() == position
        page.keyboard.press('1')
        assert image.evaluate('(i)=>Math.abs(i.getBoundingClientRect().width-i.naturalWidth)<1')
        assert page.locator('.showcase-zoom-value').inner_text() == '100%'
        viewport.focus()
        keyboard_scroll = viewport.evaluate('(v)=>v.scrollLeft')
        page.keyboard.press('Shift+ArrowRight')
        assert viewport.evaluate('(v)=>v.scrollLeft') > keyboard_scroll + 40, 'Keyboard panning did not move the image'
        assert page.locator('.showcase-counter').inner_text() == position
        page.keyboard.press('Shift+Equal')
        assert image.evaluate('(i)=>Math.abs(i.getBoundingClientRect().width-i.naturalWidth)<1'), 'Zoom enlarged the bitmap beyond its native pixels'
        assert page.get_by_role('button', name='Zoom in', exact=True).is_disabled()
        page.keyboard.press('0')
        assert not page.locator('.showcase-stage').evaluate('(s)=>s.classList.contains("is-zoomed")')
        assert viewport.evaluate('(v)=>v.scrollLeft===0 && v.scrollTop===0')
        image.dblclick(position={'x': image.bounding_box()['width'] * .7, 'y': image.bounding_box()['height'] * .55})
        assert page.locator('.showcase-stage').evaluate('(s)=>s.classList.contains("is-zoomed")')
        stage = page.locator('.showcase-stage')
        stage.dispatch_event('pointerdown', {'pointerType': 'touch', 'isPrimary': True, 'pointerId': 7, 'clientX': 250, 'clientY': 120})
        stage.dispatch_event('pointerup', {'pointerType': 'touch', 'isPrimary': True, 'pointerId': 7, 'clientX': 140, 'clientY': 125})
        assert page.locator('.showcase-counter').inner_text() == position, 'Panning switched gallery images'
        assert len(full_requests) == requests, 'Zoom downloaded another full-size image'
        page.get_by_role('button', name='Next image', exact=True).click()
        page.wait_for_function('document.querySelector(".showcase-stage").getAttribute("aria-busy")==="false"')
        assert not page.locator('.showcase-stage').evaluate('(s)=>s.classList.contains("is-zoomed")'), 'Next image retained an unusable zoom'
        assert page.get_by_role('button', name='Next image', exact=True).evaluate('(b)=>b===document.activeElement')
        page.keyboard.press('1')
        viewport.focus()
        page.keyboard.press('ArrowRight')
        page.wait_for_function('document.querySelector(".showcase-stage").getAttribute("aria-busy")==="false"')
        assert page.locator('.showcase-close').evaluate('(b)=>b===document.activeElement'), 'Navigation lost focus when the zoomed viewport was hidden'
        page.keyboard.press('Escape')
        assert opener.evaluate('(b)=>b===document.activeElement')
        assert not page.evaluate('galleryTest.errors'), page.evaluate('galleryTest.errors')
        page.close()
    report['lightbox_zoom'] = 'Viewport-filling marketing frames, translucent bottom navigation, 44px controls, actual-size/Fit/keyboard zoom, mouse pan, touch pan without slide changes, no duplicate downloads; desktop and mobile'


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--report', type=Path)
    args = parser.parse_args()
    report = {}
    with tempfile.TemporaryDirectory(prefix='otp-gallery-') as temporary:
        target = Path(temporary)
        assemble(target)
        server = ThreadingHTTPServer(('127.0.0.1', 0), functools.partial(QuietHandler, directory=str(target)))
        threading.Thread(target=server.serve_forever, daemon=True).start()
        url = f'http://127.0.0.1:{server.server_port}/'
        try:
            with sync_playwright() as playwright:
                executable = os.environ.get('GALLERY_CHROME')
                if not executable:
                    executable = next((str(path) for path in (Path('C:/Program Files/Google/Chrome/Application/chrome.exe'), Path('C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe')) if path.is_file()), None)
                browser = playwright.chromium.launch(headless=True, executable_path=executable)
                page = new_page(browser)
                desktop(page, url, report)
                page.close()
                constrained(browser, url, report)
                races_and_failures(browser, url, report)
                visibility_and_priority(browser, url, report)
                queue_and_fallback(browser, url, report)
                decode_barrier(browser, url, report)
                android_page(browser, url, report)
                lightbox(browser, url, report)
                lightbox_motion(browser, url, report)
                lightbox_zoom(browser, url, report)
                browser.close()
        finally:
            server.shutdown()
    if args.report:
        args.report.parent.mkdir(parents=True, exist_ok=True)
        args.report.write_text(json.dumps(report, indent=2) + '\n', encoding='utf-8')
    summary = dict(report)
    desktop_result = report['desktop']
    summary['desktop'] = {
        'lcp': desktop_result['lcp'], 'load': desktop_result['load'],
        'switch_commit_max_ms': max(desktop_result['switch_ms']),
        'switch_paint_max_ms': max(desktop_result['paint_switch_ms']),
        'decode_max_ms': max(entry['ms'] for entry in desktop_result['decode']),
        'image_bytes': sum(image['bytes'] for image in desktop_result['images']),
        'checks': desktop_result['checks'],
    }
    print(json.dumps(summary, indent=2))


if __name__ == '__main__':
    main()
