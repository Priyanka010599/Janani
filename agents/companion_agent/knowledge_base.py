"""Curated pregnancy knowledge for the Companion agent's RAG layer.

Ported from Services/PregnancyKnowledgeBase.cs. In a later GCP iteration
this becomes a Vertex AI Search managed index (per that file's original
header comment); for now it's the same static content injected directly.

IMPORTANT: all content is informational only. The companion always directs
users to their healthcare provider for medical decisions.
"""

_WEEK_CONTENT = {
    range(1, 5): """
Weeks 1-4 (First month): Your body is working hard even if you don't feel it yet.
The fertilized egg is implanting and the placenta is beginning to form.
You may notice a missed period, mild cramping, or spotting (implantation bleeding).
Fatigue and breast tenderness are common early signs. Start taking folic acid if you haven't.
Schedule your first prenatal appointment — usually around week 8-10.
""",
    range(5, 9): """
Weeks 5-8 (Early first trimester): Morning sickness often peaks here — though it can happen any time of day.
Small, frequent meals and ginger tea can help. Your baby's heart is beating.
Fatigue may feel overwhelming — rest as much as you can. This is normal and temporary.
Avoid raw fish, undercooked meat, unpasteurized cheeses, and alcohol.
First prenatal appointment typically happens now — includes blood tests and dating ultrasound.
""",
    range(9, 14): """
Weeks 9-13 (Late first trimester): Many women start to feel better as morning sickness eases.
The first trimester screening (nuchal translucency scan) happens around week 11-13.
Your baby is now fully formed — all major organs are in place.
You may not be showing yet, but your uterus is growing.
Energy often improves heading into the second trimester.
""",
    range(14, 21): """
Weeks 14-20 (Early second trimester): Often called the 'golden trimester' — energy returns, nausea fades.
You'll likely start showing. The anatomy scan (detailed ultrasound) is usually at week 18-22.
You may feel baby's first movements (quickening) — often described as flutters or bubbles.
Good time to start thinking about birth plans, childbirth classes, and maternity leave.
Safe exercise: walking, swimming, prenatal yoga. Listen to your body.
""",
    range(21, 28): """
Weeks 21-27 (Late second trimester): Baby's movements become stronger and more regular.
Glucose screening test (for gestational diabetes) typically done at weeks 24-28.
Back pain and round ligament pain are common. A pregnancy pillow can help with sleep.
Stay hydrated — dehydration can cause Braxton Hicks contractions (practice contractions).
Begin researching pediatricians, hospital tours, and newborn care basics.
""",
    range(28, 36): """
Weeks 28-35 (Early third trimester): Baby is gaining weight rapidly now.
Prenatal appointments become more frequent — typically every 2 weeks from week 28.
Common symptoms: shortness of breath, heartburn, swollen ankles, frequent urination.
Baby's position matters now — discuss with your provider if baby is not head-down by week 36.
Prepare your hospital bag. Know the signs of preterm labor: regular contractions before week 37,
fluid leaking, or significant pelvic pressure — contact your provider immediately.
""",
    range(36, 41): """
Weeks 36-40 (Final weeks): You're almost there. Appointments are now weekly.
Baby may 'drop' (engage into pelvis) — you may breathe more easily but feel more pelvic pressure.
Signs of labor: regular contractions that get closer together, water breaking, bloody show.
Know when to go to the hospital — your provider will give you specific guidance.
Rest, stay close to home, and trust your body. You have done an incredible thing.
""",
}

_DEFAULT_WEEK_CONTENT = "Please consult your healthcare provider for information specific to your stage of pregnancy."

_GENERAL_CONTENT = """
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
"""


def get_week_content(week: int) -> str:
    for week_range, content in _WEEK_CONTENT.items():
        if week in week_range:
            return content.strip()
    return _DEFAULT_WEEK_CONTENT


def get_general_content() -> str:
    return _GENERAL_CONTENT.strip()


# Bereavement catalog -- a full, separate replacement for the pregnancy
# knowledge base above, not an addition to it. Used only when the mother's
# profile records a loss (see UserProfile.BirthOutcome / CareContext on the
# C# side) -- the companion must not ground a grieving mother's questions in
# week-by-week pregnancy milestones or "your baby's first smile" content.
# No medication dosing (matches the app-wide rule); always defers anything
# clinical to a real provider. First-pass, minimal content -- worth review
# by a perinatal bereavement counsellor before this is relied on in
# production, same caveat as the rest of the recovery-guide content.
_BEREAVEMENT_CONTENT = """
BEREAVEMENT CARE -- for a pregnancy or infant loss (stillbirth, neonatal death, or late
miscarriage):

Grief: there is no right way to grieve and no timeline for it. Waves of grief, numbness,
anger, or guilt are all normal after a loss like this, for as long as they last.
(Source: general perinatal bereavement care guidance, e.g. NHS "Grief after baby loss";
Postpartum Support International.)

Physical recovery still matters: her body went through a birth and still needs to
recover -- wound care, watching for heavy bleeding, and rest are still important, exactly
as they would be after any birth. Keep postpartum follow-up appointments even though
there is no baby to bring to them.

Milk coming in: even after a loss, the body may still produce milk -- an unexpected and
painful reminder. Ask her provider about ways to ease this, both medication and
non-medication approaches (such as cold compresses and supportive binding); some parents
choose to pump and donate milk in their baby's memory, if and when that feels right.

Anemia and heavy bleeding (postpartum hemorrhage) follow-up matters just as much after a
loss -- don't skip the blood tests or follow-up visits a provider recommends.

Support: many parents find it helps to talk to someone trained in this kind of grief, not
only friends and family -- a perinatal loss counsellor, a support group, or a helpline.
In India, Tele-MANAS (Government of India, toll-free 14416 or 1-800-891-4416) is available
any time, in English and 20 regional languages.

If there are thoughts of self-harm, reach out immediately to a provider, a trusted person
nearby, or a helpline like Tele-MANAS above -- support is needed right now, not eventually.

Partners, and any other children in the family, are grieving too, in their own way and
their own time.
""".strip()


# Appended only for PartialLossMultiple (a multiple birth where one baby
# survived) -- IsBereaved alone can't distinguish this from a full loss, and
# the reply needs to hold both truths at once rather than either ignoring
# the surviving baby or ignoring the grief.
_PARTIAL_LOSS_ADDENDUM = """
ADDITIONAL CONTEXT -- this was a multiple birth and one baby survived. Both things are
true here: there is real grief for the baby who didn't survive, and a living baby who
needs ordinary newborn care. Grief doesn't mean she doesn't also want practical answers
about feeding, sleep, or her surviving baby's care -- answer those normally when asked,
while still leaving room for how hard it is to hold both at once.
""".strip()


def get_bereavement_content(is_partial_loss_multiple: bool = False) -> str:
    if is_partial_loss_multiple:
        return f"{_BEREAVEMENT_CONTENT}\n\n{_PARTIAL_LOSS_ADDENDUM}"
    return _BEREAVEMENT_CONTENT
