/*
 * Copyright 2023 Samsung Electronics Co., Ltd. All Rights Reserved.
 *
 * Licensed under the Apache License, Version 2.0 (the "License");
 * you may not use this file except in compliance with the License.
 * You may obtain a copy of the License at
 *
 * https://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS,
 * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 * See the License for the specific language governing permissions and
 * limitations under the License.
 */

package com.samsung.health.hrdatatransfer.data

import com.samsung.android.service.health.tracking.data.DataPoint
import com.samsung.android.service.health.tracking.data.ValueKey

class IBIDataParsing {

    companion object {
        private fun isIBIValid(ibiStatus: Int, ibiValue: Int): Boolean {
            return ibiStatus == 0 && ibiValue != 0
        }

        fun getValidIbiList(dataPoint: DataPoint): ArrayList<Int> {

            val ibiValues = dataPoint.getValue(ValueKey.HeartRateSet.IBI_LIST)
            val ibiStatuses = dataPoint.getValue(ValueKey.HeartRateSet.IBI_STATUS_LIST)

            val validIbiList = ArrayList<Int>()
            for ((i, ibiStatus) in ibiStatuses.withIndex()) {
                if (isIBIValid(ibiStatus, ibiValues[i])) {
                    validIbiList.add(ibiValues[i])
                }
            }
            return validIbiList
        }
    }
}