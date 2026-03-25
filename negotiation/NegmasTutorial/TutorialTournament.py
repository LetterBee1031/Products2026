from negmas.tournaments.neg import cartesian_tournament
from negmas.gb.negotiators.timebased import (
    BoulwareTBNegotiator,
    ConcederTBNegotiator,
    LinearTBNegotiator,
)
from negmas.inout import Scenario
from negmas.outcomes import make_issue
from negmas.outcomes.outcome_space import make_os
from negmas.preferences import LinearAdditiveUtilityFunction as U
from negmas.helpers import humanize_time
from negmas.helpers.strings import unique_name

from pathlib import Path
import time
import plotly.graph_objects as go
import numpy as np
from scipy import stats

def get_scenarios(n=2) -> list[Scenario]:
    # トーナメントで使用されるシナリオセットを生成，読み出し
    # 交渉課題
    issues = (
        make_issue([f"{i}" for i in range(10)], "quantity"),
        make_issue([f"{i}" for i in range(5)], "price"),
    )
    # 効用関数のグループを作成
    ufuns = [
        (
            U.random(issues=issues, reserved_value=(0.0, 0.6), normalized=True),
            U.random(issues=issues, reserved_value=(0.0, 0.2), normalized=True),
        )
        for _ in range(n)
    ]

    # それぞれの効用関数のための交渉シナリオを生成
    return [
        Scenario(outcome_space=make_os(issues, name=f"S{i}"), ufuns=u)
        for i, u in enumerate(ufuns)
    ]

if __name__ == "__main__":
# 交渉ごとに10秒間実施，それぞれのシナリオを10回繰り返す
    tic = time.perf_counter()
    path = Path("negmas") / unique_name("test")
    results = cartesian_tournament(
        competitors=[BoulwareTBNegotiator, ConcederTBNegotiator, LinearTBNegotiator],
        scenarios=get_scenarios(),
        mechanism_params=dict(time_limit=5), # 各交渉の時間制限，秒指定か，ラウンド数指定か
        n_repetitions=1, # 各交渉の繰り返し回数(結果には組み込まれない)
        path=path,
    )

    print(f"Done in {humanize_time(time.perf_counter()-tic, show_ms=True)}")
    print(results.scores_summary[("advantage",)])

# kdeのプロットと
    fig = go.Figure()
    strategies = results.scores["strategy"].unique()
    for strategy in strategies:
        data = (
            results.scores[results.scores["strategy"] == strategy]["advantage"]
            .dropna()
            .values
        )
        if len(data) > 1:
            kde = stats.gaussian_kde(data)
            x_range = np.linspace(data.min() - 0.5, data.max() + 0.5, 200)
            fig.add_trace(
                go.Scatter(x=x_range, y=kde(x_range), mode="lines", name=str(strategy))
            )
    fig.update_layout(
        title="Advantage Distribution by Strategy",
        xaxis_title = "Advantage",
        yaxis_title = "Density",
    )

    fig.write_image("TutorialTournament.png")