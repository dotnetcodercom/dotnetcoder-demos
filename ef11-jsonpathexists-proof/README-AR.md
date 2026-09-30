# اختبار EF Core 11: وجود خاصية JSON

هذه حزمة اختبار محلية، وليست مقالًا جاهزًا للنشر. اكتمل الاختبار الفعلي على جهازك، وتوجد أدلته في مجلد evidence.

## نتيجة الاختبار الفعلي — 30 سبتمبر 2026

نجح السكربت على Windows مع SQL Server 2025 LocalDB بإصدار `17.0.1000.7` والمثيل `(localdb)\DNC2025`، باستخدام SDK `11.0.100-rc.1.26425.128`. نجحت اختبارات EF الستة ومقارنة SQL وتنفيذ rollback، وظهر سطرا النجاح النهائيان. التحقق مبني على لقطات المستخدم؛ لم نشغّل SQL Server في بيئة إعداد الحزمة. التفاصيل في `evidence/laptop-verification.json`، وثلاث لقطات مقصوصة في `evidence/screenshots/`. لم نختبر SQL Server 2022 فعليًا.

## المتطلبات

- Windows PowerShell 5.1 أو PowerShell أحدث على Windows.
- SDK .NET 11. لديك الإصدار `11.0.100-rc.1.26425.128` الذي استخدمناه في المشروع السابق. ملف `global.json` يمنع اختيار SDK 10 بالخطأ ويسمح بإصدارات SDK 11 الأحدث.
- **SQL Server 2022 أو أحدث**، والخدمة تعمل. SQL Server 2019 أو أقدم لا يملك الدالة المطلوبة. وجود SSMS وحده لا يعني وجود محرك SQL Server.
- اتصال NuGet لاستعادة الحزمة المثبتة بالإصدار `11.0.0-rc.1.26425.128`. هذا إصدار تجريبي مثبت لإعادة الإنتاج، وليس توصية تحديث لتطبيق إنتاج.

## التشغيل

1. فك ZIP، وافتح PowerShell داخل المجلد الذي يحتوي `JsonPathProof.csproj`.
2. افتح SSMS وانظر إلى **Server name** و **Authentication** في نافذة الاتصال. استخدم الاسم نفسه في الأمر التالي.
3. إذا كنت تستخدم Windows Authentication:

```powershell
Unblock-File .\Verify-JsonPathExists.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File .\Verify-JsonPathExists.ps1 -Server '.\SQLEXPRESS'
```

غيّر فقط `'.\SQLEXPRESS'` إلى اسم الخادم في SSMS. أمثلة ممكنة: `localhost` أو `DESKTOP-NAME\SQLEXPRESS` أو `(localdb)\MSSQLLocalDB`. يجب أن تكون نسخة المحرك نفسها 2022 أو أحدث، حتى عند استخدام LocalDB.

إذا كنت تستخدم SQL Server Authentication:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\Verify-JsonPathExists.ps1 -Server '.\SQLEXPRESS' -SqlUser 'YOUR_SQL_LOGIN'
```

السكربت سيطلب كلمة المرور بصورة مخفية. لا تكتبها في الأمر ولا ترسلها في المحادثة.

## ما الذي يفعله؟

يتصل بـ `tempdb` ويستخدم جدولًا مؤقتًا اسمه `#DncJsonPathProof` داخل معاملة واحدة. يضيف ثماني حالات صغيرة ويقرأ النتائج، ثم ينفذ rollback. ينتهي الجدول المؤقت أيضًا عند إغلاق الاتصال. لا ينشئ قاعدة بيانات جديدة، ولا ينفذ `EnsureDeleted`، ولا يعدّل قواعد بيانات التطبيقات. قد يكتب المحرك سجلاته التشغيلية المعتادة في tempdb.

فحص TLS يستخدم `Encrypt=True;TrustServerCertificate=True` لمثيل التطوير المحلي؛ هذا يتجاوز التحقق من شهادة الخادم المحلية وليس إعدادًا مقترحًا لخادم إنتاج.

يتحقق الاختبار من:

- ترجمة LINQ إلى `JSON_PATH_EXISTS` وعرض SQL الفعلي الناتج.
- الخاصية المفقودة مقابل الخاصية الموجودة بقيمة JSON `null`.
- القيمة صفر والقيمة النصية: وجود المسار لا يثبت نوع القيمة ولا أنها غير null.
- المسارات المتداخلة، بما فيها قيمة `false` واسم خاصية بحالة أحرف مختلفة.
- مستند SQL `NULL`، وهو مختلف عن خاصية JSON `null`.
- النتائج الفعلية للاستعلامات ثم إزالة بيانات الاختبار.

النتائج التي أكدتها لقطات التشغيل الفعلي:

```text
Existing OptionalInt path => [2,3,4,5]
Missing OptionalInt path in non-null JSON documents => [1,6,8]
Nested Flag => [6]
Nested path => [6,8]
Different case => []
SQL NULL document => [7]
PASS: EF11 JsonPathExists proof completed
PASS: laptop verification completed
```

## ما الذي ترسله لي؟

أرسل المخرجات أو الملف `artifacts/verification.txt`. وأرسل ثلاث لقطات كاملة؛ سأقصّ الأجزاء المناسبة للمقال:

1. إصدار SDK وإصدار SQL Server ونجاح البناء.
2. SQL الناتج وجدول المقارنة بين `PathExists` و`JSON_VALUE`.
3. أسطر نجاح اختبارات EF وآخر سطرين `PASS`.

لا ترسل كلمة مرور أو connection string يحتوي بيانات دخول.

## إذا فشل

- `SQL Server 2022 ... or newer`: نسخة المحرك أقدم؛ أرسل رقم الإصدار. لا حاجة لإعادة تشغيل الاختبار مرارًا.
- SQL error `-1` أو `53` أو مهلة اتصال: راجع اسم المثيل والخدمة. جرّب الاسم الذي يعمل معك في SSMS.
- SQL error `18456`: نوع الدخول أو صلاحياته غير صحيحة. استخدم نوع Authentication نفسه في SSMS.
- SQL error `4060`: الدخول لا يمكنه فتح tempdb؛ راجع صلاحيات الدخول.
- فشل SDK أو restore/build: أرسل الخطأ كما هو، وسنصلحه قبل اختبار النتائج.

لإعادة عرض ترجمة SQL فقط، من دون اتصال بقاعدة البيانات:

```powershell
dotnet run --project .\JsonPathProof.csproj --configuration Release -- --offline
```

هذا المسار لا يثبت النتائج الفعلية على SQL Server.

## حدود الاختبار

نختبر حقول JSON نصية من نوع `nvarchar(max)` على SQL Server محلي. لا نختبر نوع `json` الأصلي في SQL Server 2025، ولا Azure SQL، ولا الأداء أو الفهارس، ولا تحويل owned/complex objects. لا يعتمد إثباتنا على EF InMemory أو SQLite.

## المصادر

- https://learn.microsoft.com/en-us/ef/core/what-is-new/ef-core-11.0/whatsnew
- https://learn.microsoft.com/en-us/sql/t-sql/functions/json-path-exists-transact-sql
- https://www.nuget.org/packages/Microsoft.EntityFrameworkCore.SqlServer/11.0.0-rc.1.26425.128
