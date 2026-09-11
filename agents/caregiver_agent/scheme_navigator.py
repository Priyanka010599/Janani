"""Government maternal/newborn entitlement info — informational only, not
authoritative or exhaustive. Exact cash amounts vary by state (Low
Performing vs High Performing states under NHM); always tell the caregiver
to confirm current figures with their local health facility/ASHA worker
rather than treating this as final.

CONTENT LAST REVIEWED: 2026-09-10 (this file's own git history is the
source of truth for when it was last checked against current scheme
rules — flagged per external review: this content had no currency marker
at all before. Covers JSY, JSSK, and UIP only; does NOT cover PMJAY
(Ayushman Bharat), PMMVY maternity benefit, or ICDS/Anganwadi nutrition
support — those are gaps in scope, not omitted by accident, and would
need their own vetted content before being added here rather than
guessed at.
"""

SCHEME_CONTENT = """
JANANI SURAKSHA YOJANA (JSY) — cash assistance for institutional delivery,
under India's National Health Mission. Rural/urban and state-performance
category change the exact amount — this varies and should be confirmed
locally, not treated as a fixed number here.

JANANI SHISHU SURAKSHA KARYAKRAM (JSSK) — zero out-of-pocket entitlement
for institutional delivery and sick newborn care, for the mother AND for
the newborn up to one year of age. Covers, free of charge, at public
health facilities:
- Free delivery, including caesarean section
- Free drugs and consumables during the stay
- Free diagnostics (blood, urine, ultrasound as needed)
- Free diet during the hospital stay
- Free referral transport, including between facilities if a transfer is needed
- Exemption from user charges

Both schemes are administered through public health facilities under the
National Health Mission. A caregiver asking about these should be pointed
toward their nearest government hospital/primary health centre or ASHA
(Accredited Social Health Activist) worker to confirm current eligibility
and amounts, since these are periodically revised and vary by state.

UNIVERSAL IMMUNIZATION PROGRAMME (UIP) — India's free government childhood
vaccination schedule, delivered at public health facilities. Rough schedule
(exact ages given as ranges; confirm with the vaccination card/ASHA worker):
- At birth: BCG, OPV (birth dose), Hepatitis B (birth dose)
- 6 weeks: OPV-1, Pentavalent-1, Rotavirus-1, PCV-1, fIPV-1
- 10 weeks: OPV-2, Pentavalent-2, Rotavirus-2
- 14 weeks: OPV-3, Pentavalent-3, Rotavirus-3, PCV-2, fIPV-2
- 9 months: Measles-Rubella (MR-1), PCV booster, Vitamin A (1st dose)
- 16-24 months: DPT booster-1, OPV booster, MR-2
All UIP vaccines are free at government health facilities. If the caregiver's
own data (given in WHO THEY CARE FOR below) shows a specific infant's
vaccines as due or overdue, defer to that — it is computed from the child's
actual age and vaccination log, not this general schedule.
"""


def get_scheme_content() -> str:
    return SCHEME_CONTENT.strip()
