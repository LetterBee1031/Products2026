from __future__ import annotations

import argparse
import csv
from datetime import datetime
from pathlib import Path
from statistics import fmean, pstdev
import sys

import matplotlib

matplotlib.use("Agg")
import matplotlib.pyplot as plt


PROJECT_ROOT = Path(__file__).resolve().parent.parent
if str(PROJECT_ROOT) not in sys.path:
    sys.path.append(str(PROJECT_ROOT))

from Server import shared_state
from negotiation import run_negotiation


DEFAULT_PROFILE_CSV = PROJECT_ROOT / "Server" / "data" / "user_profile.csv"
DEFAULT_OUTPUT_CSV = (
    PROJECT_ROOT / "Server" / "data" / "negmas_negotiation_test_results.csv"
)
DEFAULT_AVERAGE_CSV = (
    PROJECT_ROOT / "Server" / "data" / "negmas_utility_averages_100.csv"
)
DEFAULT_AVERAGE_PLOT = (
    PROJECT_ROOT / "Server" / "data" / "negmas_utility_averages_100.png"
)


def load_user_ids(profile_csv: Path) -> list[str]:
    """user_profile.csvの行順でテスト対象ユーザIDを取得する。"""
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
    current_load: float,
    max_steps: int,
    comfort_weight: float,
    random_seed: int | None,
) -> list[dict]:
    """全ユーザと交渉し、shared_stateを変更せず結果行を返す。"""
    # 内部でissue_option.csvを先に読み込み、その後user_profile.csvを反映する。
    shared_state.load_user_profiles(profile_csv)
    user_ids = load_user_ids(profile_csv)
    if not user_ids:
        raise ValueError(f"no users found in {profile_csv}")

    issue_names = list(shared_state.ISSUE_OPTIONS)
    rows: list[dict] = []

    for user_id in user_ids:
        user = shared_state.user_status[user_id]
        settings_before = shared_state.get_user_issue_settings(user_id)

        result = run_negotiation(
            user_id=user_id,
            current_load=current_load,
            max_steps=max_steps,
            comfort_weight=comfort_weight,
            random_seed=random_seed,
            persist_agreement=False,
        )

        settings_after = shared_state.get_user_issue_settings(user_id)
        if settings_after != settings_before:
            raise RuntimeError(
                f"shared_state was unexpectedly changed for user {user_id}"
            )

        # 合意なしの場合は適用予定案がないため、開始時設定を結果列へ記録する。
        result_settings = (
            result.agreement if result.agreement is not None else settings_before
        )
        final_step = result.steps[-1] if result.steps else None
        critique_count = sum(len(step.critiques) for step in result.steps)

        row = {
            "executed_at": datetime.now().isoformat(timespec="seconds"),
            "engine": result.engine,
            "user_id": user_id,
            "accepted": result.accepted,
            "reason": result.reason,
            "step_count": len(result.steps),
            "critique_count": critique_count,
            "state_unchanged": settings_after == settings_before,
            "L_before": result.initial_load,
            "L_after": result.predicted_load,
            "delta_L": result.predicted_load - result.initial_load,
            "final_U_AA": final_step.aa_utility if final_step else "",
            "final_U_PA": final_step.pa_utility if final_step else "",
            "final_tau_AA": final_step.aa_threshold if final_step else "",
            "final_tau_PA": final_step.pa_threshold if final_step else "",
        }
        row.update({f"initial_{issue}": settings_before[issue] for issue in issue_names})
        row.update({f"result_{issue}": result_settings[issue] for issue in issue_names})
        row.update({f"p_{issue}": user.p[issue] for issue in issue_names})
        row.update({f"w_{issue}": user.w[issue] for issue in issue_names})
        row.update(
            {f"coeff_{issue}": shared_state.DEFAULT_COEFFS[issue] for issue in issue_names}
        )
        row.update(
            {f"rho_{issue}": shared_state.DEFAULT_RHO[issue] for issue in issue_names}
        )
        rows.append(row)

    output_csv.parent.mkdir(parents=True, exist_ok=True)
    with output_csv.open("w", encoding="utf-8-sig", newline="") as f:
        writer = csv.DictWriter(f, fieldnames=list(rows[0]))
        writer.writeheader()
        writer.writerows(rows)

    return rows


def run_repeated_negotiations_and_plot(
    *,
    profile_csv: Path,
    output_csv: Path,
    output_plot: Path,
    trials_per_user: int = 100,
    current_load: float = 0.75,
    max_steps: int = 20,
    comfort_weight: float = 0.1,
    base_random_seed: int = 7,
) -> list[dict]:
    """各ユーザと複数回交渉し、平均効用をCSVと棒グラフへ出力する。"""
    if trials_per_user < 1:
        raise ValueError("trials_per_user must be at least 1")

    shared_state.load_user_profiles(profile_csv)
    user_ids = load_user_ids(profile_csv)
    if not user_ids:
        raise ValueError(f"no users found in {profile_csv}")

    summary_rows: list[dict] = []
    for user_index, user_id in enumerate(user_ids):
        settings_before = shared_state.get_user_issue_settings(user_id)
        aa_utilities: list[float] = []
        pa_utilities: list[float] = []
        accepted_count = 0
        step_counts: list[int] = []

        for trial_index in range(trials_per_user):
            # ユーザと試行番号から一意なseedを作り、再現可能な別候補系列にする。
            random_seed = (
                base_random_seed
                + user_index * trials_per_user
                + trial_index
            )
            result = run_negotiation(
                user_id=user_id,
                current_load=current_load,
                max_steps=max_steps,
                comfort_weight=comfort_weight,
                random_seed=random_seed,
                persist_agreement=False,
            )
            if result.steps:
                final_step = result.steps[-1]
                aa_utilities.append(final_step.aa_utility)
                pa_utilities.append(final_step.pa_utility)
            accepted_count += int(result.accepted)
            step_counts.append(len(result.steps))

            if shared_state.get_user_issue_settings(user_id) != settings_before:
                raise RuntimeError(
                    f"shared_state was unexpectedly changed for user {user_id}"
                )

        if not aa_utilities or not pa_utilities:
            raise RuntimeError(f"no utility values were produced for user {user_id}")

        summary_rows.append(
            {
                "executed_at": datetime.now().isoformat(timespec="seconds"),
                "engine": "negmas",
                "user_id": user_id,
                "trials": trials_per_user,
                "accepted_count": accepted_count,
                "acceptance_rate": accepted_count / trials_per_user,
                "mean_U_AA": fmean(aa_utilities),
                "std_U_AA": pstdev(aa_utilities),
                "mean_U_PA": fmean(pa_utilities),
                "std_U_PA": pstdev(pa_utilities),
                "mean_steps": fmean(step_counts),
                "state_unchanged": True,
                "L_current": current_load,
            }
        )

    output_csv.parent.mkdir(parents=True, exist_ok=True)
    with output_csv.open("w", encoding="utf-8-sig", newline="") as f:
        writer = csv.DictWriter(f, fieldnames=list(summary_rows[0]))
        writer.writeheader()
        writer.writerows(summary_rows)

    # ユーザごとにAAとPAの平均効用を横並びで比較する。
    labels = [row["user_id"] for row in summary_rows]
    aa_means = [row["mean_U_AA"] for row in summary_rows]
    pa_means = [row["mean_U_PA"] for row in summary_rows]
    x_positions = list(range(len(labels)))
    bar_width = 0.38

    figure, axis = plt.subplots(figsize=(10, 6))
    aa_bars = axis.bar(
        [x - bar_width / 2 for x in x_positions],
        aa_means,
        bar_width,
        label="AA utility",
    )
    pa_bars = axis.bar(
        [x + bar_width / 2 for x in x_positions],
        pa_means,
        bar_width,
        label="PA utility",
    )
    axis.set_title(f"Mean Utility by User ({trials_per_user} negotiations)")
    axis.set_xlabel("User ID")
    axis.set_ylabel("Mean utility")
    axis.set_xticks(x_positions, labels)
    axis.set_ylim(0.0, 1.05)
    axis.grid(axis="y", alpha=0.25)
    axis.legend()
    axis.bar_label(aa_bars, fmt="%.3f", padding=3, fontsize=8)
    axis.bar_label(pa_bars, fmt="%.3f", padding=3, fontsize=8)
    figure.tight_layout()

    output_plot.parent.mkdir(parents=True, exist_ok=True)
    figure.savefig(output_plot, dpi=200)
    plt.close(figure)
    return summary_rows


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(
        description=(
            "Run the NegMAS negotiation for every user without updating "
            "shared_state, then write one result row per user."
        )
    )
    parser.add_argument("--profile-csv", type=Path, default=DEFAULT_PROFILE_CSV)
    parser.add_argument("--output-csv", type=Path, default=DEFAULT_OUTPUT_CSV)
    parser.add_argument("--l-current", type=float, default=0.75)
    parser.add_argument("--max-steps", type=int, default=20)
    parser.add_argument("--comfort-weight", type=float, default=0.1)
    parser.add_argument("--random-seed", type=int, default=7)
    parser.add_argument(
        "--trials-per-user",
        type=int,
        default=1,
        help="Run repeated negotiations and plot mean utilities when greater than 1.",
    )
    parser.add_argument("--average-output-csv", type=Path, default=DEFAULT_AVERAGE_CSV)
    parser.add_argument("--average-output-plot", type=Path, default=DEFAULT_AVERAGE_PLOT)
    return parser.parse_args()


if __name__ == "__main__":
    args = parse_args()
    if args.trials_per_user > 1:
        summary = run_repeated_negotiations_and_plot(
            profile_csv=args.profile_csv,
            output_csv=args.average_output_csv,
            output_plot=args.average_output_plot,
            trials_per_user=args.trials_per_user,
            current_load=args.l_current,
            max_steps=args.max_steps,
            comfort_weight=args.comfort_weight,
            base_random_seed=args.random_seed,
        )
        print(
            f"wrote {len(summary)} user averages to {args.average_output_csv} "
            f"and {args.average_output_plot} (state unchanged)"
        )
    else:
        result_rows = run_all_users(
            profile_csv=args.profile_csv,
            output_csv=args.output_csv,
            current_load=args.l_current,
            max_steps=args.max_steps,
            comfort_weight=args.comfort_weight,
            random_seed=args.random_seed,
        )
        accepted_count = sum(bool(row["accepted"]) for row in result_rows)
        print(
            f"wrote {len(result_rows)} users to {args.output_csv} "
            f"(accepted={accepted_count}, state unchanged)"
        )
