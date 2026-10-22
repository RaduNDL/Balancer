from __future__ import annotations

import csv
import hashlib
import json
import platform
from collections import Counter
from datetime import datetime, timezone
from pathlib import Path

import joblib
import sklearn
from sklearn.feature_extraction.text import TfidfVectorizer
from sklearn.linear_model import LogisticRegression
from sklearn.metrics import (
    accuracy_score,
    classification_report,
    confusion_matrix,
    f1_score,
)
from sklearn.pipeline import FeatureUnion, Pipeline


# Fișierul este în AI_Service/scripts.
# parents[1] reprezintă directorul AI_Service.
ROOT = Path(__file__).resolve().parents[1]

DATASET_PATH = (
    ROOT / "data/labeled/balancer_onboarding_dataset_v1.csv"
)

MODEL_PATH = ROOT / "models/onboarding_v1.joblib"
REPORT_DIR = ROOT / "reports/onboarding_v1"

# Numai aceste coloane ajung în model.
# Etichetele și explicațiile nu sunt folosite ca intrări.
INPUT_COLUMNS = (
    "company_intent",
    "work_summary",
    "decision_scope",
    "team_scope",
)

# NeedsReview este un rezultat de clarificare,
# nu un rol care trebuie adăugat în CompanyRole din C#.
LABELS = [
    "Owner",
    "Admin",
    "Accountant",
    "Manager",
    "Employee",
    "NeedsReview",
]

INTENTS = {
    "create_workspace",
    "join_workspace",
    "unsure",
}


def build_text(answers: dict[str, str]) -> str:
    """Validează și combină răspunsurile într-un singur text."""

    # Folosim aceeași transformare la antrenare și predicție.
    values = {}

    for column in INPUT_COLUMNS:
        value = answers.get(column)

        if not isinstance(value, str) or not value.strip():
            raise ValueError(
                f"Missing or empty answer: {column}"
            )

        values[column] = value.strip()

    if values["company_intent"] not in INTENTS:
        raise ValueError("Invalid company_intent.")

    # Prefixele păstrează contextul fiecărui răspuns.
    return "\n".join(
        f"{column}: {values[column]}"
        for column in INPUT_COLUMNS
    )


def load_dataset(path: Path) -> list[dict[str, str]]:
    """Citește CSV-ul și verifică structura și separarea datelor."""

    # utf-8-sig acceptă și fișiere UTF-8 care au BOM.
    # newline="" permite modulului csv să gestioneze liniile corect.
    with path.open(encoding="utf-8-sig", newline="") as stream:
        reader = csv.DictReader(stream)

        required = set(INPUT_COLUMNS) | {
            "example_id",
            "scenario_group",
            "split",
            "language",
            "recommended_role",
        }

        missing = required - set(reader.fieldnames or [])

        if missing:
            raise ValueError(
                f"Missing CSV columns: {sorted(missing)}"
            )

        rows = list(reader)

    identifiers = set()
    groups = {}
    text_splits = {}

    for row in rows:
        # Detectăm rândurile cu un număr incorect de câmpuri.
        if None in row or any(
            value is None for value in row.values()
        ):
            raise ValueError("Malformed CSV row.")

        if (
            not row["example_id"]
            or row["example_id"] in identifiers
        ):
            raise ValueError(
                "Missing or duplicate example_id."
            )

        identifiers.add(row["example_id"])

        if row["recommended_role"] not in LABELS:
            raise ValueError("Unknown target label.")

        if row["split"] not in {
            "train", "validation", "test"
        }:
            raise ValueError("Invalid dataset split.")

        if (
            row["language"] != "en"
            or not row["scenario_group"]
        ):
            raise ValueError(
                "Expected English examples with scenario_group."
            )

        # Variantele aceluiași scenariu trebuie să rămână
        # în același subset și să aibă aceeași etichetă.
        group_state = (
            row["split"],
            row["recommended_role"],
        )

        previous = groups.setdefault(
            row["scenario_group"],
            group_state,
        )

        if previous != group_state:
            raise ValueError(
                "Scenario split leakage or inconsistent scenario labels."
            )

        text = build_text(row)

        # Detectăm și intrări identice aflate în subseturi diferite.
        previous_split = text_splits.setdefault(
            text,
            row["split"],
        )

        if previous_split != row["split"]:
            raise ValueError(
                "Identical input appears in different splits."
            )

    # Pentru această versiune a datasetului,
    # fiecare subset trebuie să conțină toate etichetele.
    for split in ("train", "validation", "test"):
        present = {
            row["recommended_role"]
            for row in rows
            if row["split"] == split
        }

        if present != set(LABELS):
            raise ValueError(
                f"Split {split} does not contain all labels."
            )

    return rows


def make_model(regularization: float) -> Pipeline:
    """Construiește vectorizarea și clasificatorul."""

    # Prima reprezentare folosește cuvinte și perechi de cuvinte.
    # A doua folosește fragmente de 3–5 caractere din cuvinte.
    features = FeatureUnion([
        (
            "words",
            TfidfVectorizer(
                ngram_range=(1, 2),
                sublinear_tf=True,
                strip_accents="unicode",
            ),
        ),
        (
            "characters",
            TfidfVectorizer(
                analyzer="char_wb",
                ngram_range=(3, 5),
                sublinear_tf=True,
                max_features=30000,
            ),
        ),
    ])

    # Pipeline păstrează vectorizarea și clasificatorul împreună.
    # C mai mic înseamnă regularizare mai puternică.
    return Pipeline([
        ("features", features),
        (
            "classifier",
            LogisticRegression(
                C=regularization,
                max_iter=2000,
                solver="lbfgs",
                random_state=42,
            ),
        ),
    ])


def apply_threshold(
    probabilities,
    classes,
    threshold: float,
) -> list[str]:
    """Aplică pragul pentru trimiterea către clarificare."""

    # Scorurile modelului nu sunt încredere calibrată
    # pentru utilizatori reali.
    predictions = []

    for scores in probabilities:
        top_index = int(scores.argmax())
        top_score = float(scores.max())

        if top_score < threshold:
            predictions.append("NeedsReview")
        else:
            predictions.append(str(classes[top_index]))

    return predictions


def main() -> None:
    """Execută validarea, antrenarea, evaluarea și salvarea."""

    rows = load_dataset(DATASET_PATH)

    # Respectăm separarea din CSV.
    # Nu facem o împărțire aleatorie nouă pe rânduri.
    subsets = {
        split: [
            row
            for row in rows
            if row["split"] == split
        ]
        for split in ("train", "validation", "test")
    }

    texts = {
        split: [build_text(row) for row in subset]
        for split, subset in subsets.items()
    }

    targets = {
        split: [
            row["recommended_role"]
            for row in subset
        ]
        for split, subset in subsets.items()
    }

    print(
        "Dataset loaded:",
        {
            key: len(value)
            for key, value in subsets.items()
        },
    )

    best_model = None
    best_score = -1.0
    best_threshold = 0.0
    best_c = 0.0
    trials = []

    # Încercăm trei configurații.
    # Fiecare model și vocabularul lui învață NUMAI din train.
    for regularization in (0.5, 2.0, 8.0):
        model = make_model(regularization)

        model.fit(
            texts["train"],
            targets["train"],
        )

        probabilities = model.predict_proba(
            texts["validation"]
        )

        classes = model.named_steps["classifier"].classes_

        # Alegem pragul și configurația NUMAI prin validare.
        # 0.0 reprezintă varianta fără abținere prin prag.
        for threshold in (0.0, 0.35, 0.45, 0.55):
            predicted = apply_threshold(
                probabilities,
                classes,
                threshold,
            )

            score = f1_score(
                targets["validation"],
                predicted,
                labels=LABELS,
                average="macro",
                zero_division=0,
            )

            trials.append({
                "C": regularization,
                "threshold": threshold,
                "macro_f1": float(score),
            })

            # La egalitate păstrăm prima configurație întâlnită.
            if score > best_score:
                best_model = model
                best_score = float(score)
                best_threshold = threshold
                best_c = regularization

    assert best_model is not None

    # Abia acum folosim testul.
    # Nu alegem configurația după rezultatul de test.
    probabilities = best_model.predict_proba(
        texts["test"]
    )

    classes = best_model.named_steps["classifier"].classes_

    predicted = apply_threshold(
        probabilities,
        classes,
        best_threshold,
    )

    raw_predictions = [
        str(classes[int(scores.argmax())])
        for scores in probabilities
    ]

    # Reper simplu pentru comparație:
    # creare workspace -> Owner;
    # alăturare -> Employee;
    # intenție neclară -> NeedsReview.
    baseline_mapping = {
        "create_workspace": "Owner",
        "join_workspace": "Employee",
        "unsure": "NeedsReview",
    }

    baseline = [
        baseline_mapping[row["company_intent"]]
        for row in subsets["test"]
    ]

    report = {
        "created_at_utc": datetime.now(
            timezone.utc
        ).isoformat(),

        # Amprenta identifică exact fișierul folosit.
        "dataset_sha256": hashlib.sha256(
            DATASET_PATH.read_bytes()
        ).hexdigest(),

        "python_version": platform.python_version(),
        "sklearn_version": sklearn.__version__,
        "joblib_version": joblib.__version__,
        "input_columns": list(INPUT_COLUMNS),
        "labels": LABELS,

        "split_rows": {
            key: len(value)
            for key, value in subsets.items()
        },

        "split_scenarios": {
            key: len({
                row["scenario_group"]
                for row in value
            })
            for key, value in subsets.items()
        },

        "training_class_counts": dict(
            Counter(targets["train"])
        ),

        "selected_C": best_c,
        "selected_threshold": best_threshold,
        "validation_macro_f1": best_score,
        "validation_trials": trials,

        "test_accuracy": float(
            accuracy_score(
                targets["test"],
                predicted,
            )
        ),

        "test_macro_f1": float(
            f1_score(
                targets["test"],
                predicted,
                labels=LABELS,
                average="macro",
                zero_division=0,
            )
        ),

        "baseline_test_macro_f1": float(
            f1_score(
                targets["test"],
                baseline,
                labels=LABELS,
                average="macro",
                zero_division=0,
            )
        ),

        # Ordinea liniilor și coloanelor este cea din LABELS.
        "test_confusion_matrix": confusion_matrix(
            targets["test"],
            predicted,
            labels=LABELS,
        ).tolist(),

        "test_classification_report": classification_report(
            targets["test"],
            predicted,
            labels=LABELS,
            output_dict=True,
            zero_division=0,
        ),

        "limitation": (
            "Small synthetic dataset with paired paraphrases "
            "and AI-authored labels. No real-user validation. "
            "Model scores are not calibrated confidence. "
            "Recommendations do not grant permissions."
        ),
    }

    MODEL_PATH.parent.mkdir(
        parents=True,
        exist_ok=True,
    )

    REPORT_DIR.mkdir(
        parents=True,
        exist_ok=True,
    )

    # Salvăm întregul pipeline, nu doar clasificatorul.
    # La o nouă antrenare aceste fișiere v1 sunt înlocuite.
    joblib.dump(
        {
            "pipeline": best_model,
            "threshold": best_threshold,
            "input_columns": list(INPUT_COLUMNS),
            "sklearn_version": sklearn.__version__,
            "permission_effect": "recommendation_only",
        },
        MODEL_PATH,
    )

    (REPORT_DIR / "metrics.json").write_text(
        json.dumps(report, indent=2),
        encoding="utf-8",
    )

    # Salvăm fiecare predicție de test pentru analiza greșelilor.
    with (
        REPORT_DIR / "test_predictions.csv"
    ).open(
        "w",
        encoding="utf-8",
        newline="",
    ) as stream:
        writer = csv.writer(stream)

        writer.writerow([
            "example_id",
            "scenario_group",
            "expected",
            "predicted",
            "raw_prediction",
            "top_model_score",
        ])

        for row, label, raw, scores in zip(
            subsets["test"],
            predicted,
            raw_predictions,
            probabilities,
        ):
            writer.writerow([
                row["example_id"],
                row["scenario_group"],
                row["recommended_role"],
                label,
                raw,
                float(scores.max()),
            ])

    print(
        f"Selected C: {best_c}; "
        f"threshold: {best_threshold}"
    )
    print(f"Validation macro F1: {best_score:.3f}")
    print(f"Test macro F1: {report['test_macro_f1']:.3f}")
    print(
        "Baseline test macro F1: "
        f"{report['baseline_test_macro_f1']:.3f}"
    )

    print(
        classification_report(
            targets["test"],
            predicted,
            labels=LABELS,
            zero_division=0,
        )
    )

    print(f"Model saved: {MODEL_PATH}")
    print(f"Reports saved: {REPORT_DIR}")


if __name__ == "__main__":
    # Importarea funcțiilor din alt fișier
    # nu declanșează automat antrenarea.
    main()