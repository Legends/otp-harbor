# Website indexing

The GitHub Pages deployment publishes the canonical sitemap and notifies search engines after a successful deployment.

## IndexNow

The workflow deploys the IndexNow ownership key at:

`https://legends.github.io/otp-harbor/1b6f7ab9795743588fa8e24157ad1541.txt`

After the key is reachable, the workflow submits the canonical website URL to Bing's IndexNow endpoint. IndexNow participants share submitted URLs, so one endpoint is sufficient. An IndexNow outage emits a workflow warning but does not invalidate an otherwise successful website deployment.

## Google Search Console

Google's general Indexing API is not appropriate for this website. It is restricted to pages containing `JobPosting` or livestream `BroadcastEvent` structured data. OTP Harbor instead publishes `sitemap.xml`, references it from `robots.txt`, and can submit it through the Search Console API.

The Search Console API step is enabled when both repository variables exist:

- `GOOGLE_WORKLOAD_IDENTITY_PROVIDER`
- `GOOGLE_SEARCH_CONSOLE_SERVICE_ACCOUNT`

Configure a Google Cloud Workload Identity Provider that trusts this repository, allow the named service account to use it, enable the Search Console API, and add that service-account email as a user of the `https://legends.github.io/otp-harbor/` URL-prefix property in Search Console. This OIDC configuration avoids storing a long-lived Google service-account key in GitHub.

Once configured, each successful Pages deployment submits `https://legends.github.io/otp-harbor/sitemap.xml` through the Search Console `sitemaps.submit` endpoint.
