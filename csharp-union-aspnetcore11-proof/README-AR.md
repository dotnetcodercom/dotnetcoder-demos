# ديمو C# Union Types مع ASP.NET Core 11

مشروع API واحد صغير، يستخدم `public union Pet(Dog, Cat)` الحقيقي و`LangVersion=preview`.
لا يحتاج SQL Server أو Docker أو Azure. يحتاج SDK .NET 11 والإنترنت لاستعادة حزمة OpenAPI أول مرة.
الإصدار المثبت للتكرار هو `11.0.100-rc.1.26425.128`، نفس SDK الذي استخدمته في الديمو السابق. هذه ميزة Preview.

نجح التحقق بـ23 فحصًا على Ubuntu وعلى جهاز المستخدم Windows باستخدام SDK المثبت. تأكد تشغيل Windows من سبع لقطات أرسلها المستخدم؛ يظهر فيها `ALL CHECKS PASSED (23)` و`VERIFICATION COMPLETE`. تؤكد لقطات المتصفح نجاح Dog وCat بـHTTP 200 ورفض الطلب الغامض بـHTTP 400، مع مخطط OpenAPI. توجد الأدلة في `evidence/laptop-verification.json` و`evidence/laptop-screenshots`. لم تُرسل ملفات artifacts الخام من اللابتوب، وملفات `local-*` تخص تشغيل Linux المستقل.

## 1. تشغيل التحقق

فك ZIP في مجلد جديد. افتح PowerShell داخل المجلد الذي يحتوي مباشرة على `Verify-Union.ps1` و`UnionProof.csproj`؛ لا توجد طبقة مجلد إضافية داخل ZIP.
تحقق من وجود السكربت ثم شغّله:

```powershell
Get-Item .\Verify-Union.ps1
Unblock-File .\Verify-Union.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File .\Verify-Union.ps1
```

السكربت يعيد البناء ثم يبدأ API على عنوان loopback ومنفذ متاح مؤقتًا، ويختبر HTTP الفعلي وOpenAPI، ثم يغلق API.
النجاح يظهر بالسطرين `ALL CHECKS PASSED` و`VERIFICATION COMPLETE`.
إذا فشل، أرسل نص الخطأ أو `artifacts\verification.txt`.

## 2. فتح صفحة الديمو

بعد نجاح التحقق، شغّل من نفس المجلد:

```powershell
dotnet run --project .\UnionProof.csproj -c Release --no-build --no-launch-profile -- --urls http://127.0.0.1:5111
```

افتح في المتصفح: http://127.0.0.1:5111
أزرار Send Dog وSend Cat وSend ambiguous object ترسل طلبات حقيقية إلى API.
افتح ملف OpenAPI الكامل من الرابط في الصفحة. أوقف API باستخدام Ctrl+C عند الانتهاء.
إذا كان المنفذ 5111 مستخدمًا، غيّره في الأمر وفي رابط المتصفح إلى 5112.

## 3. اللقطات للمقال

وصلت اللقطات المطلوبة، وهي كافية. المختار للمقال: `03-verification-complete.png` و`05-dog-http-200.png` و`06-ambiguous-http-400.png` داخل `evidence/laptop-screenshots`. اللقطات الأخرى محفوظة كأدلة إضافية. الخطوات التالية توضح ما نحتاجه عند إعادة التصوير فقط:

1. PowerShell: إصدار SDK، نتائج التحقق، وسطر `ALL CHECKS PASSED` ظاهر. يمكن إرسال لقطتين إذا لم تتسع النتائج.
2. صفحة المتصفح بعد Send Dog: JSON، الحالة HTTP 200، `X-Union-Case: Dog`، و`anyOf` ظاهرة.
3. الصفحة بعد Send ambiguous object: طلب فيه `breed` و`lives` والنتيجة HTTP 400، مع OpenAPI ظاهر.

أرسل اللقطات كاملة كما هي؛ سنختار ونقصّ الأجزاء المطلوبة لاحقًا. لا يوجد داعٍ لتصوير التثبيت.

## ماذا يثبت؟

- GET يعيد JSON الخاص بالحالة مباشرة، دون غلاف Union أو حقل `$type` إضافي.
- POST يختار Dog أو Cat بواسطة `JsonUnionTypeStructuralClassifier`، ويؤكد الرأس `X-Union-Case` الحالة الفعلية في C#.
- الحقول مطلوبة بواسطة `[JsonRequired]`، وتظهر في OpenAPI كـ`required`.
- الطلبات الناقصة أو الغامضة أو ذات النوع الخاطئ تُرفض بـ400.
- OpenAPI المُنشأ أثناء التشغيل يصف الطلب والرد بـ`anyOf` ومراجع مستقلة لـDog وCat.
- إعدادات JSON الافتراضية للويب تقبل `"lives":"9"` وتحوله إلى الرقم `9` في الرد. لذلك يظهر الحقل في المخطط كـ`integer` أو `string`؛ التحقق يختبر هذا التفصيل أيضًا.

**الحد المهم:** `anyOf` ليس خوارزمية اختيار الحالة. الكائن الذي يحتوي الحقول المطلوبة للحالتين قد يطابق المخططين؛ المصنّف يرفضه لأنه غامض. لا ندّعي أن كل مستند مطابق لـOpenAPI سيُقبل وقت التشغيل.

ملفات الأدلة التي ينشئها التحقق: `verification.txt` و`http-cases.txt` و`openapi.json` و`result.json` داخل `artifacts`.
المشروع لا يختبر عميلًا مولدًا أو الأداء أو MVC أو SignalR. عند تصميم عقد JSON جديد للحالات التي تتحكم بها، توصي Microsoft بالنظر في closed hierarchy مع discriminator؛ هذا الديمو يشرح عقدًا قائمًا بلا discriminator.
