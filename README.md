<div dir="rtl">

<img src="src/installer/app-256.png" width="88" alt="">

# עורך תמלול

עורך תמלול לראיונות בעברית, שרץ כולו על המחשב שלך.

מעלים הקלטה של ריאיון, והעורך:
- מתמלל אותה לעברית
- מפריד בין המראיין למרואיין
- מציג את הטקסט מסונכרן לשמע, כדי שיהיה קל לתקן

*A local Hebrew interview transcription editor for Windows: speech-to-text, speaker separation, and a transcript synced to the audio, all offline.*

## מה יש בו

- **תמלול לעברית** עם המודל [ivrit-ai/whisper-large-v3-turbo-ct2](https://huggingface.co/ivrit-ai/whisper-large-v3-turbo-ct2) דרך [faster-whisper](https://github.com/SYSTRAN/faster-whisper). מקבל שמע או וידאו: m4a, mp3, wav, mp4 ועוד.
- **הפרדת דוברים:** מראיין ומרואיין, בלי מודל נוסף. העורך מזהה את הקולות לפי טביעת קול (MFCC) ומחלק אותם לשני דוברים. על ראיונות אמיתיים כ-98.6% מהמילים שויכו לדובר הנכון. לחיצה על שם הדובר מתקנת שיוך שגוי.
- **סימון מילים לבדיקה:** מילים שהמודל לא בטוח בהן מסומנות ב-(?) ומופיעות ברשימה "לבדיקה".
- **עורך מסונכרן לשמע:** המילה שמתנגנת מודגשת, לחיצה על מילה קופצת אליה בהקלטה, וכל עריכה נשמרת אוטומטית לקובץ טקסט רגיל.
- **התקנה פשוטה:** Installer ל-Windows שלא צריך הרשאות מנהל ולא דורש להתקין שום דבר נוסף, כי פייתון והמודל כלולים בו. העורך רץ ברקע עם סמל ליד השעון.

## פרטיות

הכול רץ על המחשב:
- ההקלטות והתמלולים לא עולים לאינטרנט, והתמלול עובד גם בלי חיבור.
- השרת המקומי מאזין רק ל-`127.0.0.1`.

## בנייה מהקוד

צריך:
- Windows 10/11 (64 ביט)
- Python 3.12 עם הספרייה `faster-whisper` (`pip install faster-whisper`)
- המודל: להוריד את [ivrit-ai/whisper-large-v3-turbo-ct2](https://huggingface.co/ivrit-ai/whisper-large-v3-turbo-ct2) לתיקייה `src/model`

הרצה בלי התקנה, לפיתוח:

```
python -X utf8 src/app/server.py --open
```

בניית ה-Installer:

```
python src/build.py
```

הפקודה יוצרת את `dist/התקנת עורך תמלול.exe`. ה-Installer, המפעיל וכלי ההסרה כתובים ב-C# ונבנים עם הקומפיילר שמגיע עם Windows (‎.NET Framework 4), בלי להתקין כלום.

## מבנה

| תיקייה | מה יש בה |
| --- | --- |
| `src/app/` | העורך: השרת (`server.py`), התמלול (`transcribe.py`), הפרדת הדוברים (`diarize.py`), הדף (`index.html`) |
| `src/installer/` | ה-Installer, המפעיל (סמל ליד השעון) וכלי ההסרה |
| `src/build.py` | בונה את התיקייה המותקנת ואת ה-Installer |

## תודות

- [ivrit-ai](https://huggingface.co/ivrit-ai) על מודל התמלול לעברית
- [faster-whisper](https://github.com/SYSTRAN/faster-whisper) ו-[CTranslate2](https://github.com/OpenNMT/CTranslate2)
- [PyAV](https://github.com/PyAV-Org/PyAV)
- אייקונים: [Lucide](https://lucide.dev)
- גופנים: [Rubik](https://github.com/googlefonts/rubik), [Noto Sans Hebrew](https://github.com/notofonts/hebrew)

הרישיונות שלהם מפורטים ב-[THIRD_PARTY.md](THIRD_PARTY.md).

---

נבנה על ידי **[יהלי מילרמן](https://www.instagram.com/yali.millerman/)**

</div>
