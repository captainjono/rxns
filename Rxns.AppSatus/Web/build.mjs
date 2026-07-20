// Replaces the legacy Grunt pipeline with a single Node script.
// Produces:
//   dist/app.full.min.js   = all <script> tags from index.html (except data-concat="false")
//                            concatenated in order, followed by a $templateCache.put(...)
//                            entry per partial .html file.
//   dist/app.full.min.css  = compiled app.less followed by every <link rel="stylesheet">
//                            in index.html (except data-concat="false").
//   dist/index.html        = index.html with tagged <script>/<link> stripped and two
//                            <script src="app.full.min.js">/<link href="app.full.min.css">
//                            appended.
//
// Usage:
//   npm install                   # installs less + chokidar (tiny deps)
//   npm run build                 # one-shot build
//   npm run watch                 # watches JS/LESS/HTML + rebuilds on change
//   npm run serve                 # watch + static file server on :9001
//
// Parity notes vs the old Gruntfile:
//  - Skipped: jshint, ngmin, uglify, htmlmin, imagemin. The previous Grunt config had
//    these commented out too (see grunt.registerTask('build', ...)) — the emitted
//    bundle has never been minified in practice, so we match current behaviour.
//  - karma: kept out. Re-introduce if tests start being authored again.

import { readFileSync, writeFileSync, mkdirSync, statSync, existsSync, readdirSync, copyFileSync } from 'node:fs';
import { dirname, join, relative, resolve as pathResolve } from 'node:path';
import { createServer } from 'node:http';
import { fileURLToPath } from 'node:url';
import less from 'less';

const WEB = dirname(fileURLToPath(import.meta.url));
const DIST = join(WEB, 'dist');

// ---- small helpers ------------------------------------------------------

const read = (p) => readFileSync(p, 'utf8');
const write = (p, s) => {
    mkdirSync(dirname(p), { recursive: true });
    writeFileSync(p, s);
};

// Extract <script src="..."> entries, honouring data-concat="false".
function extractScripts(html) {
    const out = [];
    const re = /<script\b([^>]*)>/gi;
    let m;
    while ((m = re.exec(html)) !== null) {
        const attrs = m[1];
        const src = attrs.match(/\bsrc\s*=\s*"([^"]+)"/i)?.[1];
        if (!src) continue;
        if (/data-concat\s*=\s*"false"/i.test(attrs)) continue;
        // Drop absolute URLs (livereload etc) — only bundle local files.
        if (/^https?:\/\//i.test(src)) continue;
        out.push(src);
    }
    return out;
}

// Extract <link rel="stylesheet" href="..."> entries (not stylesheet/less), honouring data-concat="false".
function extractStylesheets(html) {
    const out = [];
    const re = /<link\b([^>]*)>/gi;
    let m;
    while ((m = re.exec(html)) !== null) {
        const attrs = m[1];
        const rel = attrs.match(/\brel\s*=\s*"([^"]+)"/i)?.[1]?.toLowerCase();
        if (rel !== 'stylesheet') continue; // skip stylesheet/less (runtime-compiled)
        const href = attrs.match(/\bhref\s*=\s*"([^"]+)"/i)?.[1];
        if (!href) continue;
        if (/data-concat\s*=\s*"false"/i.test(attrs)) continue;
        if (/^https?:\/\//i.test(href)) continue;
        out.push(href);
    }
    return out;
}

// Scan top-level source dirs (mirrors the old createFolderGlobs exclusion set)
// for *.html partials. Excludes index.html + _SpecRunner.
const IGNORE_DIRS = new Set(['node_modules', 'bower_components', 'shared_components', 'dist', 'temp', '.grunt']);
function findPartials() {
    const results = [];
    const walk = (dir) => {
        for (const name of readdirSync(dir)) {
            if (name.startsWith('.')) continue;
            const full = join(dir, name);
            const rel = relative(WEB, full).replace(/\\/g, '/');
            const top = rel.split('/')[0];
            if (IGNORE_DIRS.has(top)) continue;
            const s = statSync(full);
            if (s.isDirectory()) walk(full);
            else if (s.isFile() && name.endsWith('.html') && name !== '_SpecRunner.html') {
                results.push(rel);
            }
        }
    };
    walk(WEB);
    return results;
}

// Compile a single HTML partial into a $templateCache.put('<path>', '<escaped>') line.
// Path convention matches the old grunt-angular-templates output: paths are used verbatim
// as the templateUrl the Angular controllers reference.
function templateCacheEntry(templatePath, html) {
    // Strip the leading "./" if present; use forward slashes (matches existing bundle usage).
    const key = templatePath.replace(/^\.?\//, '').replace(/\\/g, '/');
    // Collapse whitespace lightly (parity with the "no htmlmin" Grunt step; just make the
    // output bundle small enough to diff). We preserve quotes and attribute content verbatim.
    const body = html
        .replace(/\r\n?/g, '\n')
        .replace(/\\/g, '\\\\')
        .replace(/'/g, "\\'")
        .replace(/\n/g, "\\n' +\n        '");
    return `  $templateCache.put('${key}',\n    '${body}'\n  );\n`;
}

function buildTemplatesModule(moduleName, partials) {
    const parts = partials.map((p) => templateCacheEntry(p, read(join(WEB, p)))).join('\n');
    return `// Auto-generated by build.mjs — do not edit.
angular.module('${moduleName}').run(['$templateCache', function($templateCache) {
${parts}
}]);
`;
}

// ---- LESS compile -------------------------------------------------------

async function compileLess() {
    const appLess = read(join(WEB, 'app.less'));
    const out = await less.render(appLess, {
        filename: join(WEB, 'app.less'),
        paths: [WEB],
        compress: false,
    });
    return out.css;
}

// ---- main build ---------------------------------------------------------

async function build() {
    const start = Date.now();

    const indexHtml = read(join(WEB, 'index.html'));
    const pkg = JSON.parse(read(join(WEB, 'package.json')));
    const moduleName = pkg.name || 'portal';

    const scripts = extractScripts(indexHtml);
    const stylesheets = extractStylesheets(indexHtml);

    // --- JS bundle ---
    const jsParts = [];
    for (const src of scripts) {
        const p = join(WEB, src);
        if (!existsSync(p)) {
            console.warn(`  [skip] missing script: ${src}`);
            continue;
        }
        jsParts.push(`/* --- ${src} --- */\n${read(p)}\n`);
    }
    const partials = findPartials();
    jsParts.push(buildTemplatesModule(moduleName, partials));
    write(join(DIST, 'app.full.min.js'), jsParts.join('\n'));

    // --- CSS bundle ---
    const cssParts = [];
    cssParts.push(await compileLess());
    for (const href of stylesheets) {
        const p = join(WEB, href);
        if (!existsSync(p)) {
            console.warn(`  [skip] missing stylesheet: ${href}`);
            continue;
        }
        cssParts.push(`/* --- ${href} --- */\n${read(p)}\n`);
    }
    write(join(DIST, 'app.full.min.css'), cssParts.join('\n'));

    // --- dist/index.html ---
    // Strip every <script> and <link rel="stylesheet"> we bundled, then inject two references.
    // We do this by removing anything tagged with data-concat!="false" in the head/body.
    let distIndex = indexHtml
        // remove <script> without data-concat="false" and without http(s) src.
        // Special case: also strip less.js (the runtime LESS compiler) because
        // dist ships the precompiled app.full.min.css — no runtime compile needed,
        // and leaving less.js in means it'd chase every @import as an HTTP fetch
        // against paths that aren't in dist.
        .replace(/<script\b([^>]*)>[\s\S]*?<\/script>\s*/gi, (m, attrs) => {
            const src = attrs.match(/\bsrc\s*=\s*"([^"]+)"/i)?.[1] ?? '';
            if (/less\.js/i.test(src)) return ''; // strip runtime LESS in dist
            if (/data-concat\s*=\s*"false"/i.test(attrs)) return m;
            if (/^https?:\/\//i.test(src)) return m;
            if (!src) return m; // inline script — leave alone
            return '';
        })
        // remove <link> tags whose rel is "stylesheet" (bundled into
        // app.full.min.css) or "stylesheet/less" (the dev-time LESS source —
        // redundant in dist now that less.js is stripped). Keep icons, manifests,
        // and anything tagged data-concat="false".
        .replace(/<link\b([^>]*)>\s*/gi, (m, attrs) => {
            const rel = attrs.match(/\brel\s*=\s*"([^"]+)"/i)?.[1]?.toLowerCase();
            if (rel === 'stylesheet/less') return ''; // runtime LESS removed
            if (rel !== 'stylesheet') return m;       // keep icons, manifests, etc.
            if (/data-concat\s*=\s*"false"/i.test(attrs)) return m;
            const href = attrs.match(/\bhref\s*=\s*"([^"]+)"/i)?.[1] ?? '';
            if (/^https?:\/\//i.test(href)) return m;
            return '';
        });
    distIndex = distIndex
        .replace('</head>', '  <link rel="stylesheet" href="app.full.min.css">\n  </head>');

    // Augment load.js must run AFTER app.full.min.js (which bundles angular +
    // the portal module). The source puts it before </body> for readability;
    // strip it from its source position and re-emit immediately AFTER the
    // bundle script tag so infraStatus.js et al. see `angular` defined.
    distIndex = distIndex.replace(/<script\b[^>]*\bsrc\s*=\s*"augment\/load\.js"[^>]*><\/script>\s*/gi, '');
    distIndex = distIndex.replace('</body>',
        '  <script src="app.full.min.js"></script>\n' +
        '  <script src="augment/load.js" onerror="this.remove()" data-concat="false"></script>\n' +
        '</body>');
    write(join(DIST, 'index.html'), distIndex);

    // --- Copy img/ + the whole bower_components/ tree.
    // The runtime LESS compiler (less.js, kept by data-concat="false") follows
    // @import chains starting at app.less, which transitively pull
    // bower_components/bootstrap/less/*.less and friends. Trying to enumerate
    // the @import graph here is brittle — copying the lot is reliable and
    // bounded (~tens of MB once, not per-request). Also covers any other
    // runtime asset (fonts, icons, animation gifs) whose path lives inside a
    // less/css/js file rather than a top-level index.html tag.
    copyDir(join(WEB, 'img'), join(DIST, 'img'));
    const bowerSrc = join(WEB, 'bower_components');
    if (existsSync(bowerSrc)) copyDir(bowerSrc, join(DIST, 'bower_components'));

    // --- Copy any bower_components asset referenced by a NON-bundled script/link
    // tag in the SOURCE index.html. Two cases stay as separate file references in
    // the dist:
    //   1. <script src="..." data-concat="false">  (e.g. less.js for runtime LESS)
    //   2. <link rel="stylesheet/less"|"icon"|...>  (any rel that's not "stylesheet")
    // Both kept by the strip-and-inject passes above; the dist needs the actual
    // files at the same relative path.
    const refs = new Set();
    const scriptRes = [
        /<script\b[^>]*\bdata-concat\s*=\s*"false"[^>]*\bsrc\s*=\s*"([^"]+)"/gi,
        /<script\b[^>]*\bsrc\s*=\s*"([^"]+)"[^>]*\bdata-concat\s*=\s*"false"/gi,
    ];
    for (const re of scriptRes) {
        let m;
        while ((m = re.exec(indexHtml)) !== null) {
            const src = m[1];
            if (/^https?:\/\//i.test(src)) continue;
            if (src.startsWith('augment/')) continue; // augment overlay served from a separate static root
            refs.add(src);
        }
    }
    const linkRe = /<link\b[^>]*\bhref\s*=\s*"([^"]+)"[^>]*\brel\s*=\s*"([^"]+)"/gi;
    let lm;
    while ((lm = linkRe.exec(indexHtml)) !== null) {
        const href = lm[1];
        const rel = (lm[2] || '').toLowerCase();
        if (rel === 'stylesheet') continue; // bundled into app.full.min.css
        if (/^https?:\/\//i.test(href)) continue;
        refs.add(href);
    }
    const ensureCopy = (from, to) => {
        mkdirSync(dirname(to), { recursive: true });
        copyFileSync(from, to);
    };
    for (const src of refs) {
        const from = join(WEB, src);
        const to = join(DIST, src);
        if (existsSync(from)) {
            ensureCopy(from, to);
            // Mirror typical sibling files some libs need (e.g. less.js source maps).
            const srcMap = from + '.map';
            if (existsSync(srcMap)) ensureCopy(srcMap, to + '.map');
        } else {
            console.warn(`  [skip-copy] missing data-concat="false" asset: ${src}`);
        }
    }

    // --- Mirror into the theBFG embedded-resource location so rebuilding the C#
    // project picks up the new bundle without an extra manual copy step. The arena
    // serves app.full.min.js/css from src/TestArena/ as EmbeddedResource (see
    // src/theBFG.csproj). If that path isn't checked in locally we just skip.
    const BFG_EMBED = pathResolve(WEB, '..', '..', 'src', 'TestArena');
    if (existsSync(BFG_EMBED)) {
        copyFileSync(join(DIST, 'app.full.min.js'),  join(BFG_EMBED, 'app.full.min.js'));
        copyFileSync(join(DIST, 'app.full.min.css'), join(BFG_EMBED, 'app.full.min.css'));
    }

    const elapsed = Date.now() - start;
    const jsKB = Math.round(read(join(DIST, 'app.full.min.js')).length / 1024);
    const cssKB = Math.round(read(join(DIST, 'app.full.min.css')).length / 1024);
    console.log(`build ok in ${elapsed} ms  (js ${jsKB} KB, css ${cssKB} KB, ${partials.length} partials)`);
}

function copyDir(src, dst) {
    if (!existsSync(src)) return;
    mkdirSync(dst, { recursive: true });
    for (const name of readdirSync(src)) {
        const s = join(src, name);
        const d = join(dst, name);
        const st = statSync(s);
        if (st.isDirectory()) copyDir(s, d);
        else copyFileSync(s, d);
    }
}

// ---- watch / serve ------------------------------------------------------

async function watch() {
    await build();
    const { default: chokidar } = await import('chokidar');
    const watcher = chokidar.watch(['**/*.js', '**/*.less', '**/*.html'], {
        cwd: WEB,
        ignored: (p) => IGNORE_DIRS.has(p.split(/[\\/]/)[0]),
        ignoreInitial: true,
    });
    let t;
    const debounced = (changed) => {
        clearTimeout(t);
        t = setTimeout(async () => {
            try {
                await build();
                console.log(`  ^ rebuilt after change: ${changed}`);
            } catch (e) {
                console.error('build failed:', e.message);
            }
        }, 150);
    };
    watcher.on('all', (_, p) => debounced(p));
    console.log('watching for changes…');
}

async function serve(port = 9001) {
    await build();
    // Dead-simple static file server rooted at WEB so bower_components/* stay reachable
    // (dev mode loads from source, not from dist).
    const mime = { '.html':'text/html', '.js':'application/javascript', '.css':'text/css',
        '.less':'text/css', '.json':'application/json', '.png':'image/png', '.jpg':'image/jpeg',
        '.svg':'image/svg+xml', '.woff':'font/woff', '.woff2':'font/woff2', '.ttf':'font/ttf',
        '.eot':'application/vnd.ms-fontobject', '.ogg':'audio/ogg' };
    createServer((req, res) => {
        const url = decodeURIComponent((req.url || '/').split('?')[0]);
        const target = join(WEB, url === '/' ? 'index.html' : url);
        if (!target.startsWith(WEB) || !existsSync(target)) {
            res.writeHead(404); res.end('not found'); return;
        }
        const st = statSync(target);
        if (st.isDirectory()) { res.writeHead(404); res.end('no index'); return; }
        const ext = (target.match(/\.[^.]+$/)?.[0] || '').toLowerCase();
        res.writeHead(200, { 'Content-Type': mime[ext] || 'application/octet-stream' });
        res.end(readFileSync(target));
    }).listen(port, () => console.log(`serving http://localhost:${port}`));
    // Run watch alongside so edits rebuild dist/
    watch();
}

// Shim npm-installed vendor libs into bower_components/ paths so the existing
// index.html <script> references still resolve. Runs as `postinstall`. New
// vendor libs: add an entry to VENDOR_SHIMS rather than editing index.html.
const VENDOR_SHIMS = [
    { from: 'node_modules/@uirouter/angularjs/release/angular-ui-router.js',
      to:   'bower_components/angular-ui-router/release/angular-ui-router.js' },
    { from: 'node_modules/angular-loading-bar/build/loading-bar.min.js',
      to:   'bower_components/angular-loading-bar/build/loading-bar.min.js' },
    { from: 'node_modules/fuse.js/dist/fuse.basic.js',
      to:   'bower_components/fuse/fuse.basic.js' },
];
function shim() {
    let copied = 0;
    for (const { from, to } of VENDOR_SHIMS) {
        const src = join(WEB, from);
        const dst = join(WEB, to);
        if (!existsSync(src)) { console.warn(`  [shim] missing source: ${from}`); continue; }
        if (existsSync(dst)) continue;
        mkdirSync(dirname(dst), { recursive: true });
        copyFileSync(src, dst);
        copied++;
    }
    console.log(`shim ok (${copied} copied, ${VENDOR_SHIMS.length - copied} already present)`);
}

// ---- cli ----------------------------------------------------------------

const cmd = process.argv[2] || 'build';
const target = { build, watch, serve, shim }[cmd];
if (!target) {
    console.error(`unknown command: ${cmd}. use one of: build, watch, serve, shim`);
    process.exit(2);
}
Promise.resolve(target()).catch((e) => { console.error(e); process.exit(1); });
