from contextlib import asynccontextmanager
from pathlib import Path
from typing import Literal
import math

import joblib
import sklearn
from fastapi import FastAPI, Request
from pydantic import BaseModel, ConfigDict, Field


# main.py este în AI_Service/src/balancer_ai/api.
# parents[3] reprezintă directorul AI_Service.
ROOT = Path(__file__).resolve().parents[3]

MODEL_PATH = ROOT / "models/onboarding_v1.joblib"

INPUT_COLUMNS = (
    "company_intent",
    "work_summary",
    "decision_scope",
    "team_scope",
)

Role = Literal[
    "Owner",
    "Admin",
    "Accountant",
    "Manager",
    "Employee",
    "NeedsReview",
]


class OnboardingAnswers(BaseModel):
    """Datele acceptate de endpointul de predicție."""

    # Eliminăm spațiile exterioare și respingem câmpurile suplimentare.
    model_config = ConfigDict(
        extra="forbid",
        str_strip_whitespace=True,
    )

    company_intent: Literal[
        "create_workspace",
        "join_workspace",
        "unsure",
    ]

    work_summary: str = Field(
        min_length=5,
        max_length=2000,
    )

    decision_scope: str = Field(
        min_length=5,
        max_length=2000,
    )

    team_scope: str = Field(
        min_length=5,
        max_length=2000,
    )


class Recommendation(BaseModel):
    """Structura răspunsului trimis către ASP.NET Core."""

    recommended_role: Role
    raw_prediction: Role

    needs_clarification: bool
    clarification_question: str | None

    suggested_modules: list[str]
    explanation: str

    next_action: Literal[
        "CreateWorkspace",
        "RequestApproval",
        "Clarify",
    ]

    permission_effect: Literal[
        "recommendation_only"
    ] = "recommendation_only"

    model_version: str = "onboarding_v1"


class Predictor:
    """Încarcă modelul și produce recomandările."""

    def __init__(self, path: Path):
        # Încărcăm numai modelul local produs de scriptul nostru.
        # Fișierele joblib din surse necunoscute pot executa cod.
        bundle = joblib.load(path)

        if bundle["sklearn_version"] != sklearn.__version__:
            raise RuntimeError(
                "Use the same scikit-learn version as training."
            )

        if tuple(bundle["input_columns"]) != INPUT_COLUMNS:
            raise RuntimeError(
                "Model input schema does not match the API."
            )

        if bundle.get("permission_effect") != "recommendation_only":
            raise RuntimeError("Unexpected model purpose.")

        self.pipeline = bundle["pipeline"]
        self.threshold = float(bundle["threshold"])

        if (
            not math.isfinite(self.threshold)
            or not 0 <= self.threshold <= 1
        ):
            raise RuntimeError("Invalid model threshold.")

        self.classes = self.pipeline.named_steps[
            "classifier"
        ].classes_

        expected_labels = {
            "Owner",
            "Admin",
            "Accountant",
            "Manager",
            "Employee",
            "NeedsReview",
        }

        if set(self.classes) != expected_labels:
            raise RuntimeError("Unexpected model labels.")

    def recommend(
        self,
        answers: OnboardingAnswers,
    ) -> Recommendation:
        # Păstrăm aceeași transformare ca în train_model.py.
        values = answers.model_dump()

        text = "\n".join(
            f"{column}: {values[column]}"
            for column in INPUT_COLUMNS
        )

        scores = self.pipeline.predict_proba([text])[0]

        index = int(scores.argmax())
        raw = str(self.classes[index])

        if float(scores[index]) >= self.threshold:
            role = raw
        else:
            role = "NeedsReview"

        # Owner poate descrie doar crearea unui workspace nou.
        # Nu recomandăm preluarea unei firme existente.
        if (
            role == "Owner"
            and answers.company_intent != "create_workspace"
        ):
            role = "NeedsReview"

        modules = {
            "Owner": [
                "Company workspace",
                "Team members",
                "Business overview",
            ],
            "Admin": [
                "Workspace settings",
                "Team members",
                "Shared records",
            ],
            "Accountant": [
                "Invoices",
                "Expenses",
                "Financial reports",
            ],
            "Manager": [
                "Projects",
                "Tasks",
                "Team planning",
            ],
            "Employee": [
                "My tasks",
                "Time entries",
                "Assigned resources",
            ],
            "NeedsReview": [],
        }

        explanations = {
            "Owner": (
                "You described creating a new workspace "
                "for your business."
            ),
            "Admin": (
                "Your answers focus on workspace configuration "
                "and access administration."
            ),
            "Accountant": (
                "Your answers focus on financial records, "
                "payments and reporting."
            ),
            "Manager": (
                "Your answers focus on coordinating people, "
                "priorities and delivery."
            ),
            "Employee": (
                "Your answers focus on completing "
                "individually assigned work."
            ),
            "NeedsReview": (
                "The model needs a clearer description before "
                "recommending one primary profile."
            ),
        }

        # Întrebări suplimentare alese după predicția inițială.
        questions = {
            "Manager": (
                "Do you regularly assign work and manage "
                "deadlines for other people?"
            ),
            "Accountant": (
                "Do you maintain company financial records, "
                "or only submit your own expenses?"
            ),
            "Admin": (
                "Do you administer this business workspace, "
                "or only maintain technical systems?"
            ),
            "Owner": (
                "Are you creating a new workspace, or joining "
                "a company that already has one?"
            ),
            "Employee": (
                "Do you mainly complete your own tasks, "
                "or coordinate other people?"
            ),
            "NeedsReview": (
                "What are your two main weekly responsibilities, "
                "and which is your primary focus?"
            ),
        }

        if role == "NeedsReview":
            next_action = "Clarify"
        elif role == "Owner":
            next_action = "CreateWorkspace"
        else:
            next_action = "RequestApproval"

        return Recommendation(
            recommended_role=role,
            raw_prediction=raw,
            needs_clarification=role == "NeedsReview",
            clarification_question=(
                questions[raw]
                if role == "NeedsReview"
                else None
            ),
            suggested_modules=modules[role],
            explanation=explanations[role],
            next_action=next_action,
        )


@asynccontextmanager
async def lifespan(app: FastAPI):
    # Modelul se încarcă o singură dată la pornirea procesului.
    app.state.predictor = Predictor(MODEL_PATH)

    yield

    # Eliberăm referința la oprire.
    app.state.predictor = None


app = FastAPI(
    title="Balancer AI Service",
    version="1.0.0",
    lifespan=lifespan,
)


@app.get("/health")
def health():
    """Permite verificarea pornirii serviciului."""

    return {
        "status": "ready",
        "model_version": "onboarding_v1",
    }


@app.post(
    "/predict",
    response_model=Recommendation,
)
def predict(
    answers: OnboardingAnswers,
    request: Request,
):
    # Endpointul sincron rulează în poolul de fire FastAPI.
    # Nu modifică utilizatori sau roluri în SQL Server.
    return request.app.state.predictor.recommend(answers)