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
    # All 19 previews, including initially lazy bottom cards, prepare after critical load.
    page.wait_for_function('galleryTest.decode.filter(e=>e.src.includes("/previews/")).length>=19')
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
    assert len(requests) == before, 'Prepared switches caused new network requests'
    assert page.evaluate('galleryTest.decode.length') == before_decode, 'Prepared switches decoded again'
    assert page.evaluate('originalCards.every((c,i)=>c===document.querySelectorAll("#showcase-grid button")[i])'), 'Cards were recreated'
    timings = page.evaluate('performance.getEntriesByName("gallery-switch").map(e=>e.duration)')
    assert max(timings) < 100, timings
    assert max(paint_timings) < 100, paint_timings
    assert not any('/framed/' in request for request in requests), 'Full-size gallery image requested at startup'
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
    page.route('**/framed/desktop/dark/account-list.png', lambda route: held.append(route))
    first = page.locator('#showcase-grid button').first
    first.focus()
    page.keyboard.press('Enter')
    page.wait_for_function('document.querySelector("dialog").open')
    wait_for_routes(page, held, 1)
    assert held and page.locator('dialog img').is_hidden()
    assert page.get_by_text('Loading full-size image…', exact=True).is_visible()
    held.pop().continue_()
    page.wait_for_function('document.querySelector("dialog img").naturalWidth===1920 && !document.querySelector("dialog img").hidden')
    assert page.locator('dialog .showcase-close').evaluate('(e)=>e===document.activeElement')
    page.keyboard.press('Escape')
    assert not page.locator('dialog').evaluate('(d)=>d.open')
    assert first.evaluate('(e)=>e===document.activeElement'), 'Dialog did not restore focus'
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
    page.route('**/framed/android/dark/account-list.png', lambda route: route.abort())
    page.locator('#showcase-grid button').first.click()
    page.get_by_role('button', name='Retry full-size image', exact=True).wait_for(state='visible')
    assert page.locator('dialog img').is_hidden()
    page.unroute('**/framed/android/dark/account-list.png')
    page.get_by_role('button', name='Retry full-size image', exact=True).click()
    page.wait_for_function('document.querySelector("dialog img").naturalWidth===1920 && !document.querySelector("dialog img").hidden')
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
        assert not any('/assets/android/' in req or '/framed/' in req for req in requests)
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
    page.route('**/framed/desktop/light/account-list.png', lambda route: held.append(route))
    page.locator('#showcase-grid button').first.click()
    page.wait_for_timeout(100)
    page.keyboard.press('Escape')
    assert held
    held.pop().continue_()
    page.wait_for_timeout(100)
    assert not page.locator('dialog').evaluate('(d)=>d.open')
    page.close()
    report['decode_barrier'] = 'Successful download cannot reveal a group before decode; late full-size completion cannot reopen dialog'


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
