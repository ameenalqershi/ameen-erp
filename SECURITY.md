# Security policy | سياسة الأمان

## Project maturity

WOW ERP is under active development and is **not certified or recommended for production deployment**. Financial data and identity configuration are security-sensitive. No security support SLA is promised.

## Report a vulnerability privately

Please **do not open a public Issue, Discussion, or Pull Request with exploit details, credentials, customer information, or unpatched vulnerabilities**. Prefer GitHub's private vulnerability reporting feature **if enabled** under the repository Security tab. If that feature is unavailable, contact the owner privately using contact options on their GitHub profile and ask for a secure reporting channel before sending exploit details.

Include a concise affected component, impact, safe reproduction steps using synthetic data, and suggested mitigation if known. Do not probe instances you do not own or operate without authorization.

## Support window

No formal stable release or production security-support window exists. Maintainers will assess the report when available; response and remediation times are not guaranteed.

## Safety for contributors

- Never commit secrets, production connection strings, tokens, real personal data, certificates, SQL backups, or credentials.
- Use `backend/.env.example` placeholders only and store real settings outside Git.
- Run SQL tests against a disposable test server only.
- Keep OIDC issuer/audience and branch/company access checks in tests.
- Scrub logs and GitHub Actions artifacts before publication.
- If a secret is exposed, revoke or rotate it through the provider and privately notify a maintainer. Deleting a commit does not revoke the secret.

Arabic: الثغرات الأمنية تُبلغ سرًا، وليس عبر Issues العامة. المشروع غير معتمد للإنتاج، ولا تستخدم بيانات العملاء أو كلمات مرور حقيقية في الاختبارات.
