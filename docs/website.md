# The website

The static site for **snip.e-path.co.uk** lives in `site/`. It follows Panehouse and Total Git's
family layout, using Snip's dark palette, the blue accent and coral annotation colour. It has no
build step, JavaScript, external fonts or external page dependencies.

| What | Where |
|---|---|
| Page and styles | `site/index.html`, `site/style.css` |
| SVG logo and generated pictures | `site/img/` |
| Image generator | `python tools/make_site_images.py` |
| Headers, download redirect and 404s | `site-host/conf/default.conf` |
| Container | `site-host/compose.yaml`: `nginx:alpine`, `snip-site`, Pi port **8099** |
| Deployment | `pwsh -File scripts/deploy-site.ps1` |

Site, hosting, image-generator and design-only changes are ignored by the app release workflow.

## Pictures

The page uses real app screenshots from `docs/screenshots/`:

- `editor.png`: editor with an annotated demo capture → `site/img/editor.webp`.
- `steps.png`: numbered steps, highlighter and pixelate → `site/img/steps.webp`.
- `capture.png`: the frozen capture overlay during a region drag → `site/img/capture.webp`.

Use only demo content, without real paths, accounts, client content or private information. The
script scales images down to at most 1600 px wide, preserving their aspect ratios, and saves WebP
at quality 90. Images are not enlarged. Missing source screenshots are reported and skipped, so
icons and a branded social preview can be generated before screenshots are ready. The page
references all three captures; generate them before publishing. The deploy script checks that
the required assets exist.

The script also copies `design/logo/mark-dark.svg` and `snip-logo-dark.svg` into `site/img/`,
rasterises those SVGs with headless Edge/Chrome, and uses Pillow to make `favicon.png` (64×64),
`apple-touch-icon.png` (180×180, opaque dark background) and `og.png` (1200×630). The social
preview uses the editor shot when it exists; otherwise it shows the brand and pitch, without a
mock screenshot. Rerun it after changing the logo or captures. It needs Python with Pillow and
Edge or Chrome installed; set `BROWSER_EXE` if the browser isn't in a standard location. Browser
rendering uses an isolated temporary profile, separate from your own tabs.

The website is dark only. It uses the canonical dark-background SVGs, whose wordmark lettering
is drawn as paths. Light variants stay in `design/logo/` for other uses.

## Local preview

Run `python -m http.server 8765 --directory site`, then open **http://localhost:8765**. Root paths
such as `/style.css` require a server rather than opening the HTML directly. Check desktop and
phone widths. Python's server only serves files: `/download` and the custom 404 status/page are
nginx behaviour and cannot be tested with this preview server.

## Publishing

The prepared destination is `pgj99@192.168.0.120:~/docker/snip-site` on the Pi 5, alongside
Panehouse on 8098. `scripts/deploy-site.ps1` stages the static site and hosting configuration,
copies them over SSH, refills `html/` and `conf/` in place, then checks/reloads nginx or starts
the container on the first deployment. Directory bind mounts retain their identity during a
redeploy. It needs the existing SSH key. Optional parameters are `-PiHost` and `-Dir`; `Dir`
accepts a simple home-relative path such as `~/docker/snip-site`. Nothing publishes on push.

`/download` returns a temporary redirect to
`https://github.com/paulepath/snip/releases/latest/download/SnipApp-win-Setup.exe`.
Missing paths return the home page with HTTP **404**, following Panehouse's setup. nginx sends
the same content-security, referrer-policy and content-type headers, and caches images for seven days.

## Routing to arrange separately

The files prepare the site for **snip.e-path.co.uk**; they do not configure the network:

- NAS Traefik: HTTPS router for `snip.e-path.co.uk` → `http://192.168.0.120:8099`, and HTTP → HTTPS redirect.
- Public DNS/TLS: use the existing `*.e-path.co.uk` home-hosting setup.
- LAN DNS: arrange the local rewrite to the NAS Traefik address, as for Panehouse.

The footer's **Built by E-PATH DIGITAL** link goes to <https://www.e-path.co.uk>. **Source on
GitHub** links the public repository; no licence is claimed. The case-study link to
<https://www.e-path.co.uk/work/snip> is commented out until Paul approves the page. Uncomment
it only when the case study is live.
