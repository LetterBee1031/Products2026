from __future__ import annotations

import argparse
import csv
import io
from contextlib import redirect_stdout
from datetime import datetime
from math import exp
from pathlib import Path
import sys


PROJECT_ROOT = Path(__file__).resolve().parent.parent
if str(PROJECT_ROOT) not in sys.path:
    sys.path.append(str(PROJECT_ROOT))

from Server import shared_state
from negotiation.TestNegotiation1 import (
    AAParams,
    PAProfile,
    RuleBasedLoadModel,
    Thresholds,
    U_AA,
    U_PA,
    U_PA_load,
    U_PA_pref,
    change_cost,
    d_in,
    d_out,
    run_example,
)


DEFAULT_PROFILE_CSV = PROJECT_ROOT / "Server" / "data" / "user_profile.csv"
DEFAULT_OUTPUT_CSV = PROJECT_ROOT / "Server" / "data" / "negotiation_utility_results.csv"


def build_pa_profile(user_id: str) -> PAProfile:
    # shared_state.load_user_profiles() 後の user_status から、
    # TestNegotiation1.py の効用計算で使う PAProfile を組み立てる。
    user = shared_state.user_status[user_id]
    return PAProfile(
        p=dict(user.p),
        w=dict(user.w),
        tau_accept=0.70,
        tau_min=0.60,
        lambda_L=0.30,
        eta=6.0,
    )


def calculate_utilities(
    *,
    L_current: float,
    current_setting: dict[str, float],
    result_setting: dict[str, float],
    pa_profile: PAProfile,
) -> dict[str, float]:
    # run_example() は合意後の設定を返すだけなので、
    # CSVに記録したい効用値はここで同じ係数・rhoを使って再計算する。
    thresholds = Thresholds(L_obs_low=0.3, L_obs_high=0.7, margin=0.10)
    load_model = RuleBasedLoadModel(a_coeffs=dict(shared_state.DEFAULT_COEFFS))
    aa_params = AAParams(alpha=10.0, beta=2.0, gamma=1.0, lam=0.5)

    L_after = load_model.predict(L_current, current_setting, result_setting)
    u_pa, _ = U_PA(
        result_setting,
        L_current=L_current,
        current_setting=current_setting,
        load_model=load_model,
        thresholds=thresholds,
        profile=pa_profile,
    )
    u_aa, _ = U_AA(
        result_setting,
        L_current=L_current,
        current_setting=current_setting,
        load_model=load_model,
        thresholds=thresholds,
        rho=dict(shared_state.DEFAULT_RHO),
        params=aa_params,
    )

    return {
        "L_before": L_current,
        "L_after": L_after,
        "delta_L": L_after - L_current,
        "U_PA": u_pa,
        "U_PA_pref": U_PA_pref(result_setting, pa_profile),
        "U_PA_load": U_PA_load(L_after, thresholds, pa_profile),
        "U_AA": u_aa,
        "U_AA_out": exp(
            -aa_params.alpha
            * d_out(
                L_pred=L_after,
                low=thresholds.L_pred_low,
                high=thresholds.L_pred_high,
            )
        ),
        "U_AA_in": exp(
            -aa_params.beta
            * d_in(
                L_pred=L_after,
                low=thresholds.L_pred_low,
                high=thresholds.L_pred_high,
            )
        ),
        "U_AA_change": exp(
            -aa_params.gamma
            * change_cost(
                current_setting=current_setting,
                offer=result_setting,
                rho=dict(shared_state.DEFAULT_RHO),
            )
        ),
    }


def load_user_ids(profile_csv: Path) -> list[str]:
    # 実行対象ユーザの順序は user_profile.csv の行順に合わせる。
    with profile_csv.open("r", encoding="utf-8-sig", newline="") as f:
        reader = csv.DictReader(f)
        if reader.fieldnames is None or "user_id" not in reader.fieldnames:
            raise ValueError("user_profile.csv must contain a 'user_id' column")
        return [
            (row.get("user_id") or "").strip()
            for row in reader
            if (row.get("user_id") or "").strip()
        ]


def run_all_users(
    *,
    profile_csv: Path,
    output_csv: Path,
    L_current: float,
    n_steps: int,
    max_candidates: int,
    quiet: bool,
) -> None:
    # load_user_profiles() internally loads issue_option.csv first, then user_profile.csv.
    # そのため、この呼び出し以降は ISSUE_OPTIONS / DEFAULT_COEFFS / DEFAULT_RHO も
    # issue_option.csv の内容に更新されている。
    shared_state.load_user_profiles(profile_csv)
    user_ids = load_user_ids(profile_csv)
    if not user_ids:
        raise ValueError(f"no users found in {profile_csv}")

    rows = []
    # 論点名は固定リストではなく issue_option.csv から読み込まれたものを使う。
    # これにより、論点を増減しても testRun.py 側の修正を少なくできる。
    issue_names = list(shared_state.ISSUE_OPTIONS.keys())

    for user_id in user_ids:
        user = shared_state.user_status[user_id]
        initial_setting = dict(shared_state.DEFAULT_ISSUE_SETTINGS)
        pa_profile = build_pa_profile(user_id)

        if quiet:
            # run_example() は詳細をprintするため、通常実行では抑制してCSV出力だけにする。
            with redirect_stdout(io.StringIO()):
                result_setting = run_example(
                    L_current=L_current,
                    current_setting=initial_setting,
                    pa_preference=dict(user.p),
                    pa_weight=dict(user.w),
                    n_steps=n_steps,
                    max_candidates=max_candidates,
                )
        else:
            # --show-run-log 指定時は、各ユーザの交渉ログもそのまま表示する。
            result_setting = run_example(
                L_current=L_current,
                current_setting=initial_setting,
                pa_preference=dict(user.p),
                pa_weight=dict(user.w),
                n_steps=n_steps,
                max_candidates=max_candidates,
            )

        utilities = calculate_utilities(
            L_current=L_current,
            current_setting=initial_setting,
            result_setting=result_setting,
            pa_profile=pa_profile,
        )

        row = {
            "executed_at": datetime.now().isoformat(timespec="seconds"),
            "user_id": user_id,
        }
        # 初期設定・交渉後設定・ユーザプロファイル・グローバル設定を同じ行に残す。
        # 後から効用値の差を見たとき、どの条件で走った結果か追跡しやすくするため。
        row.update({f"initial_{key}": initial_setting[key] for key in issue_names})
        row.update({f"result_{key}": result_setting[key] for key in issue_names})
        row.update({f"p_{key}": user.p[key] for key in issue_names})
        row.update({f"w_{key}": user.w[key] for key in issue_names})
        row.update({f"coeff_{key}": shared_state.DEFAULT_COEFFS[key] for key in issue_names})
        row.update({f"rho_{key}": shared_state.DEFAULT_RHO[key] for key in issue_names})
        row.update({key: round(value, 6) for key, value in utilities.items()})
        rows.append(row)

    output_csv.parent.mkdir(parents=True, exist_ok=True)
    with output_csv.open("w", encoding="utf-8-sig", newline="") as f:
        writer = csv.DictWriter(f, fieldnames=list(rows[0].keys()))
        writer.writeheader()
        writer.writerows(rows)

    print(f"wrote {len(rows)} users to {output_csv}")


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(
        description="Run TestNegotiation1.run_example for all users using user_profile.csv and issue_option.csv."
    )
    parser.add_argument("--profile-csv", type=Path, default=DEFAULT_PROFILE_CSV)
    parser.add_argument("--output-csv", type=Path, default=DEFAULT_OUTPUT_CSV)
    parser.add_argument("--l-current", type=float, default=0.75)
    parser.add_argument("--n-steps", type=int, default=20)
    parser.add_argument("--max-candidates", type=int, default=15000)
    parser.add_argument(
        "--show-run-log",
        action="store_true",
        help="Show run_example print output for each user.",
    )
    return parser.parse_args()


if __name__ == "__main__":
    args = parse_args()
    run_all_users(
        profile_csv=args.profile_csv,
        output_csv=args.output_csv,
        L_current=0.25,
        n_steps=args.n_steps,
        max_candidates=args.max_candidates,
        quiet=not args.show_run_log,
    )
