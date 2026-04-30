package com.example.biodata_from_watch.sensor

import java.net.NetworkInterface

object DeviceNetwork {
    fun localIpAddress(): String {
        // サーバ保存用に、時計自身のIPv4アドレスを取得する。取れない場合は unknown にする。
        return runCatching {
            NetworkInterface.getNetworkInterfaces().toList()
                .flatMap { it.inetAddresses.toList() }
                .firstOrNull { address ->
                    !address.isLoopbackAddress && address.hostAddress?.contains(":") == false
                }
                ?.hostAddress
                ?: "unknown"
        }.getOrDefault("unknown")
    }
}
