// Services/PregnancyKnowledgeBase.cs
// Curated pregnancy knowledge for the AI companion's RAG layer.
// In GCP deployment, this feeds into Vertex AI Search as a managed index.
// Locally, it's used directly as context injection per query.
//
// IMPORTANT: all content is informational only. The companion always directs
// users to their healthcare provider for medical decisions.

namespace Janani.Services;

public static class PregnancyKnowledgeBase
{
    public static string GetWeekContent(int week) => week switch
    {
        >= 1 and <= 4 => """
            Weeks 1-4 (First month): Your body is working hard even if you don't feel it yet.
            The fertilized egg is implanting and the placenta is beginning to form.
            You may notice a missed period, mild cramping, or spotting (implantation bleeding).
            Fatigue and breast tenderness are common early signs. Start taking folic acid if you haven't.
            Schedule your first prenatal appointment — usually around week 8-10.
            """,

        >= 5 and <= 8 => """
            Weeks 5-8 (Early first trimester): Morning sickness often peaks here — though it can happen any time of day.
            Small, frequent meals and ginger tea can help. Your baby's heart is beating.
            Fatigue may feel overwhelming — rest as much as you can. This is normal and temporary.
            Avoid raw fish, undercooked meat, unpasteurized cheeses, and alcohol.
            First prenatal appointment typically happens now — includes blood tests and dating ultrasound.
            """,

        >= 9 and <= 13 => """
            Weeks 9-13 (Late first trimester): Many women start to feel better as morning sickness eases.
            The first trimester screening (nuchal translucency scan) happens around week 11-13.
            Your baby is now fully formed — all major organs are in place.
            You may not be showing yet, but your uterus is growing.
            Energy often improves heading into the second trimester.
            """,

        >= 14 and <= 20 => """
            Weeks 14-20 (Early second trimester): Often called the 'golden trimester' — energy returns, nausea fades.
            You'll likely start showing. The anatomy scan (detailed ultrasound) is usually at week 18-22.
            You may feel baby's first movements (quickening) — often described as flutters or bubbles.
            Good time to start thinking about birth plans, childbirth classes, and maternity leave.
            Safe exercise: walking, swimming, prenatal yoga. Listen to your body.
            """,

        >= 21 and <= 27 => """
            Weeks 21-27 (Late second trimester): Baby's movements become stronger and more regular.
            Glucose screening test (for gestational diabetes) typically done at weeks 24-28.
            Back pain and round ligament pain are common. A pregnancy pillow can help with sleep.
            Stay hydrated — dehydration can cause Braxton Hicks contractions (practice contractions).
            Begin researching pediatricians, hospital tours, and newborn care basics.
            """,

        >= 28 and <= 35 => """
            Weeks 28-35 (Early third trimester): Baby is gaining weight rapidly now.
            Prenatal appointments become more frequent — typically every 2 weeks from week 28.
            Common symptoms: shortness of breath, heartburn, swollen ankles, frequent urination.
            Baby's position matters now — discuss with your provider if baby is not head-down by week 36.
            Prepare your hospital bag. Know the signs of preterm labor: regular contractions before week 37,
            fluid leaking, or significant pelvic pressure — contact your provider immediately.
            """,

        >= 36 and <= 40 => """
            Weeks 36-40 (Final weeks): You're almost there. Appointments are now weekly.
            Baby may 'drop' (engage into pelvis) — you may breathe more easily but feel more pelvic pressure.
            Signs of labor: regular contractions that get closer together, water breaking, bloody show.
            Know when to go to the hospital — your provider will give you specific guidance.
            Rest, stay close to home, and trust your body. You have done an incredible thing.
            """,

        _ => "Please consult your healthcare provider for information specific to your stage of pregnancy."
    };

    public static string GetGeneralContent() => """
        GENERAL PREGNANCY INFORMATION:

        Nutrition: Aim for balanced meals with protein, complex carbohydrates, healthy fats, and plenty of vegetables.
        Key nutrients: folic acid, iron, calcium, omega-3s, vitamin D. Prenatal vitamins fill gaps.
        Foods to avoid: raw/undercooked meat and eggs, raw fish/shellfish, unpasteurized dairy,
        high-mercury fish (shark, swordfish, king mackerel), alcohol, excess caffeine (max 200mg/day).

        Exercise: Generally safe and beneficial. Walking, swimming, prenatal yoga, and low-impact exercise
        are excellent choices. Avoid contact sports, activities with fall risk, and lying flat on your back
        after the first trimester. Always check with your provider before starting a new exercise routine.

        Sleep: Sleep on your left side from the second trimester onward to improve blood flow to baby.
        A pregnancy pillow between your knees can help. Afternoon naps are completely valid.

        Emotional wellbeing: Mood changes are normal due to hormonal shifts. Anxiety about pregnancy,
        birth, and parenthood is very common. If feelings of sadness or anxiety persist, speak with
        your provider — perinatal mental health support is important and available.

        Work: Know your rights regarding maternity leave and workplace accommodations.
        Communicate with your employer when you feel comfortable doing so.
        Take breaks, stay hydrated, and don't hesitate to ask for what you need.

        WARNING SIGNS — contact your provider immediately if you experience:
        Heavy bleeding, severe abdominal pain, severe headache with vision changes,
        sudden swelling of face/hands, decreased or absent fetal movement after week 28,
        fever over 38°C (100.4°F), or regular contractions before week 37.
        """;
}