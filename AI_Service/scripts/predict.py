import argparse
import json

import joblib
import sklearn

# Cele două scripturi sunt în același director.
# Importăm transformarea folosită și la antrenare.
from train_model import MODEL_PATH, build_text


def main() -> None:
    """Încarcă modelul și clasifică un set de răspunsuri."""

    parser = argparse.ArgumentParser()

    parser.add_argument(
        "--interactive",
        action="store_true",
    )

    args = parser.parse_args()

    if not MODEL_PATH.exists():
        raise FileNotFoundError(
            "Train the model before running predictions."
        )

    # Încărcăm doar modelul generat de propriul script.
    # Un fișier joblib dintr-o sursă necunoscută poate executa cod.
    bundle = joblib.load(MODEL_PATH)

    if bundle["sklearn_version"] != sklearn.__version__:
        raise RuntimeError(
            "Use the same scikit-learn version as training."
        )

    if args.interactive:
        # Întrebările aplicației sunt în engleză.
        answers = {
            "company_intent": input(
                "Intent "
                "(create_workspace/join_workspace/unsure): "
            ).strip(),

            "work_summary": input(
                "Describe your main work activities: "
            ).strip(),

            "decision_scope": input(
                "What decisions are you responsible for? "
            ).strip(),

            "team_scope": input(
                "How do you work with other people? "
            ).strip(),
        }

    else:
        # Exemplu pentru verificarea fluxului de predicție.
        answers = {
            "company_intent": "join_workspace",

            "work_summary": (
                "I check supplier invoices, reconcile payments "
                "and prepare monthly financial statements."
            ),

            "decision_scope": (
                "I maintain financial records, "
                "not user access or project assignments."
            ),

            "team_scope": (
                "I work with the finance team."
            ),
        }

    pipeline = bundle["pipeline"]

    text = build_text(answers)

    scores = pipeline.predict_proba([text])[0]

    classes = pipeline.named_steps["classifier"].classes_

    top_index = int(scores.argmax())
    raw_prediction = str(classes[top_index])
    top_score = float(scores[top_index])

    # Pragul este cel ales prin validare, nu unul ales manual aici.
    if top_score >= bundle["threshold"]:
        recommendation = raw_prediction
    else:
        recommendation = "NeedsReview"

    result = {
        "recommended_role": recommendation,
        "raw_prediction": raw_prediction,
        "top_model_score": round(top_score, 4),
        "needs_clarification": recommendation == "NeedsReview",

        # Predicția nu atribuie roluri în SQL Server.
        "permission_effect": "recommendation_only",

        # Nu prezentăm scorul ca certitudine verificată.
        "scores_are_calibrated": False,

        "class_scores": {
            str(label): round(float(score), 4)
            for label, score in zip(classes, scores)
        },
    }

    print(json.dumps(result, indent=2))


if __name__ == "__main__":
    main()