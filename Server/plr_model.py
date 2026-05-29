from __future__ import annotations

from typing import Iterable, Sequence

import numpy as np
from sklearn.base import BaseEstimator, RegressorMixin
from sklearn.metrics import mean_squared_error
from sklearn.model_selection import GridSearchCV


class PLRRegressor(BaseEstimator, RegressorMixin):
    """
    PLRモデル:
        d(Y) = a * exp(-b * Y) + c

    scikit-learn の Estimator 形式に合わせた回帰モデル。
    GridSearchCV が a, b, c を差し替えながら評価する。
    """

    def __init__(self, a: float = 2.0, b: float = 6.0, c: float = 3.0):
        self.a = a
        self.b = b
        self.c = c

    def fit(self, X, y):
        # このモデル自体は閉形式・反復学習を持たない。
        # GridSearchCV 側がパラメータ探索を行う。
        return self

    def predict(self, X):
        y_luminance = np.asarray(X, dtype=float).reshape(-1)
        return self.a * np.exp(-self.b * y_luminance) + self.c


def fit_plr_model(samples: Sequence[dict], cv: int = 3, step: float = 0.1) -> dict:
    """
    キャリブレーションデータからPLRモデルの a,b,c を推定する。

    Parameters
    ----------
    samples:
        [{"luminanceY_cam": 0.5, "pupilMm": 3.2}, ...]
    cv:
        GridSearchCV の交差検証分割数。
        サンプル数が少ない場合は内部で自動的に下げる。
    step:
        a,b,c の探索刻み。

    Returns
    -------
    dict:
        {"a": ..., "b": ..., "c": ..., "mse": ..., "sampleCount": ...}
    """

    if len(samples) < 10:
        raise ValueError("Calibration samples are too few.")

    X = np.array([[s["luminanceY_cam"]] for s in samples], dtype=float)
    y = np.array([s["pupilMm"] for s in samples], dtype=float)

    # NaN / infを除外する
    valid_mask = np.isfinite(X.reshape(-1)) & np.isfinite(y)
    X = X[valid_mask]
    y = y[valid_mask]

    if len(y) < 10:
        raise ValueError("Valid calibration samples are too few.")

    # 要件の範囲に合わせる。
    # a: [1, 4], b: [4, 8], c: [0, 8]
    param_grid = {
        "a": np.arange(1.0, 4.0 + step / 2.0, step),
        "b": np.arange(4.0, 8.0 + step / 2.0, step),
        "c": np.arange(0.0, 8.0 + step / 2.0, step),
    }

    # cvはサンプル数を超えないようにする。
    cv = max(2, min(cv, len(y)))

    grid_search = GridSearchCV(
        estimator=PLRRegressor(),
        param_grid=param_grid,
        scoring="neg_mean_squared_error",
        cv=cv,
        n_jobs=-1,
    )

    grid_search.fit(X, y)
    model: PLRRegressor = grid_search.best_estimator_

    predicted = model.predict(X)
    mse = mean_squared_error(y, predicted)

    return {
        "a": float(model.a),
        "b": float(model.b),
        "c": float(model.c),
        "mse": float(mse),
        "sampleCount": int(len(y)),
    }


def calculate_luminance_correlation(samples: Sequence[dict]) -> dict:
    """
    PLRキャリブレーションサンプル内の luminanceY と luminanceY_cam の
    Pearson相関係数を計算する。

    Parameters
    ----------
    samples:
        [{"luminanceY": 0.5, "luminanceY_cam": 0.48, ...}, ...]

    Returns
    -------
    dict:
        {"correlation": ..., "sampleCount": ...}
    """

    if len(samples) < 2:
        raise ValueError("輝度サンプル数が少なすぎます。")

    luminance_y = np.array([s["luminanceY_panel"] for s in samples], dtype=float)
    luminance_y_cam = np.array([s["luminanceY_cam"] for s in samples], dtype=float)

    valid_mask = np.isfinite(luminance_y) & np.isfinite(luminance_y_cam)
    luminance_y = luminance_y[valid_mask]
    luminance_y_cam = luminance_y_cam[valid_mask]

    if len(luminance_y) < 2:
        raise ValueError("有効な輝度サンプル数が少なすぎます。")

    if np.std(luminance_y) == 0 or np.std(luminance_y_cam) == 0:
        raise ValueError("輝度値が一定のため、相関係数を計算できません。")

    correlation = np.corrcoef(luminance_y, luminance_y_cam)[0, 1]

    return {
        "correlation": float(correlation),
        "sampleCount": int(len(luminance_y)),
    }


def predict_pupil_diameter(luminance_y, a: float, b: float, c: float):
    """輝度Yから予測瞳孔径 d(Y) を計算する。"""
    return PLRRegressor(a=a, b=b, c=c).predict(luminance_y).tolist()
