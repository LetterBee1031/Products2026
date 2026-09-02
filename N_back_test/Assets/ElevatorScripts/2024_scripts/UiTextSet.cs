using UnityEngine;
using System;
using System.Collections;
using System.Collections.Generic;
public enum LoadType
{
    High,
    Medium,
    Low,
    None,
}

public enum DisasterType
{
    fire,
    rain,
    earth,
    elect,
}

public class UiTextSet
{
    public string beforeExp;
    public string InsideDisplay;
    public string stopFloor;
    public string OutsideDisplay;

    public UiTextSet(string beforeExp, string InsideDisplay, string stopFloor, string OutsideDisplay)
    {
        this.beforeExp = beforeExp;
        this.InsideDisplay = InsideDisplay;
        this.stopFloor = stopFloor;
        this.OutsideDisplay = OutsideDisplay;
    }
    public UiTextSet(UiTextSet uiTextSet)
    {
        this.beforeExp = uiTextSet.beforeExp;
        this.InsideDisplay = uiTextSet.InsideDisplay;
        this.stopFloor = uiTextSet.stopFloor;
        this.OutsideDisplay = uiTextSet.OutsideDisplay;
    }

}

public class DisasterTextSet 
{
    public Dictionary<LoadType, UiTextSet> fireTextSets;
    public Dictionary<LoadType, UiTextSet> rainTextSets;
    public Dictionary<LoadType, UiTextSet> earthTextSets;
    public Dictionary<LoadType, UiTextSet> electTextSets;
    
    public DisasterTextSet()
    {
        fireTextSets = new Dictionary<LoadType, UiTextSet>();
        rainTextSets = new Dictionary<LoadType, UiTextSet>();
        earthTextSets = new Dictionary<LoadType, UiTextSet>();
        electTextSets = new Dictionary<LoadType, UiTextSet>();

        fireTextSets.Add(
            LoadType.High, 
            new UiTextSet(
                "走行中、火災の信号を受けると「火災です。避難階へ\n止まります。」のアナウンスと共に避難階（基本は一階）\nまでエレベーターが走行し、扉を一定時間開きます。\n火災信号解除後、エレベーターは復旧(ふっきゅう)します。", 
                "火災管制運転が行われている間は\n「火災管制運転中」「避難階へとまります」\nと表示され、エレベーターが特別な運転をしている\nことが、乗っている人に伝えられます。", 
                "火災管制運転時は、建物の管理者が設定した避難階\n（基本1階）にエレベーターが移動し扉が開きます。", 
                "火災管制運転中は「火災」と表示され、\nエレベーターが特別な運転をしていることが、\nエレベーターの外の人にも伝えられます。"
            )
        );
        
        fireTextSets.Add(
            LoadType.Medium, 
            new UiTextSet(
                "火災時は、避難階（基本は1階）までエレベーターが走行\nし、扉を一定時間開きます。火災信号解除後、エレベーターは復旧します。", 
                "火災管制運転が行われている間は\n「火災管制運転中」「避難階へと止まります」\nと表示されます。", 
                "火災管制運転時は、建物の管理者が設定した避難階\n（基本1階）にエレベーターが移動します。", 
                "火災管制運転中は「火災」と表示されます。"
            )
        );

        fireTextSets.Add(
            LoadType.Low, 
            new UiTextSet(
                "火災時は、避難階までエレベーターが走行し、扉を開きます。", 
                "火災時は、特別な表示になります。", 
                "火災時は、建物の管理者が設定した避難階に\nエレベーターが移動します。", 
                "火災時は，特別な表示になります。"
            )
        );

        fireTextSets.Add(
            LoadType.None, 
            new UiTextSet(
                " ", 
                " ", 
                " ", 
                " "
            )
        );



        rainTextSets.Add(
            LoadType.High, 
            new UiTextSet(
                "走行中、冠水スイッチが冠水を感知すると「冠水です。\n管制運転を行います。」のアナウンスと共に最寄りの階まで\nエレベーターが走行し、扉を開きます。最寄りの階が最下階\nであった場合、一つ上の階へ移動した後、扉が開きます。", 
                "冠水管制運転が行われている間は\n「冠水管制運転中」\nと表示され、エレベーターが特別な運転をしている\nことが、乗っている人に伝えられます。", 
                "冠水管制運転時は最寄りの階に停止し、扉が\n開きます。このとき、最寄りの階が最下階（1階）の\n場合は一つ上の階に移動してから扉が開きます。\n\n冠水時は建物の下層が浸水している可能性がある\nので、下の階には避難せず係員の指示に従って避難\nしましょう。", 
                "冠水管制運転中は「冠水」と表示され、\nエレベーターが特別な運転をしていることが、\nエレベーターの外の人にも伝えられます。"
            )
        );
        
        rainTextSets.Add(
            LoadType.Medium, 
            new UiTextSet(
                "冠水時は、最寄りの階までエレベーターが走行し、扉を\n開きます。最寄りの階が最下階であった場合、一つ上の階へ\n移動し、扉を開きます。", 
                "冠水管制運転が行われている間は\n「冠水管制運転中」と表示されます。", 
                "冠水管制運転時は、最寄りの階に停止します。\nこのとき、最寄りの階が最下階（1階）の場合は一つ\n上の階に移動します。\n冠水時は、下の階には避難せず係員の指示に従って\n避難しましょう。", 
                "冠水管制運転中は「冠水」と表示されます。"
            )
        );

        rainTextSets.Add(
            LoadType.Low, 
            new UiTextSet(
                "冠水時は、最寄りの階までエレベーターが走行し、扉を\n開きます。", 
                "冠水時は、特別な表示になります。", 
                "冠水時は、最寄りの階に停止します。このとき、\n下の階には避難せず、係員の指示に従って避難\nしましょう。", 
                "冠水時は，特別な表示になります。"
            )
        );

        rainTextSets.Add(
            LoadType.None, 
            new UiTextSet(
                " ", 
                " ", 
                " ", 
                " "
            )
        );


        earthTextSets.Add(
            LoadType.High, 
            new UiTextSet(
                "走行中、地震をセンサーが感知すると、「地震です。最寄り\nの階へ止まります。」というアナウンスと共にエレベーターnが最寄りの階まで自動で走行し、扉を一定時間開きます。\n乗客が降りた後、扉を閉め待機します。約震度4以下の地震\nの場合は、一定時間が過ぎると自動で復旧します。\n約震度4より大きい地震の場合は自動で復旧せず、係員が\n安全確認をした後に復旧します。", 
                "地震管制運転が行われている間は\n「地震管制運転中」「最寄りの階へとまります」\nと表示され、エレベーターが特別な運転をしている\nことが、乗っている人に伝えられます。", 
                "地震管制運転時は、最寄りの階に停止し、扉が\n開きます。", 
                "地震管制運転中は「地震」と表示され、\nエレベーターが特別な運転をしていることが、\nエレベーターの外の人にも伝えられます。"
            )
        );
        
        earthTextSets.Add(
            LoadType.Medium, 
            new UiTextSet(
                "地震時は、最寄りの階までエレベーターが走行し、扉を\n一定時間開きます。乗客が降りた後、扉を閉め待機します。\n約震度4以下の地震の場合は、一定時間が過ぎると自動で\n復旧します。約震度4より大きい地震の場合は自動で復旧\nせず、係員が安全確認をした後に復旧します。", 
                "地震管制運転が行われている間は\n「地震管制運転中」「最寄りの階へとまります」\nと表示されます。", 
                "地震管制運転時は、最寄りの階に停止します。", 
                "地震管制運転中は「地震」と表示されます。"
            )
        );

        earthTextSets.Add(
            LoadType.Low, 
            new UiTextSet(
                "地震時は、最寄りの階まで、エレベーターが走行し、扉を\n開きます。", 
                "地震時は、特別な表示になります。", 
                "地震時は、最寄りの階に停止します。", 
                "地震時は，特別な表示になります。"
            )
        );

        earthTextSets.Add(
            LoadType.None, 
            new UiTextSet(
                " ", 
                " ", 
                " ", 
                " "
            )
        );

        



        electTextSets.Add(
            LoadType.High, 
            new UiTextSet(
                "走行中、停電が発生した場合、「停電です。救出運転中\nです。」というアナウンスと共にエレベーターを最寄りの階\nまで自動で走行させます。最寄り階に到着したら、一定時間\n扉を開きます。止まる階は乗客の人数によって、上の最寄り\nの階か、下の最寄りの階かが決まります。かご内の天井の\n明かりは停電灯に切り替わり、真っ暗にはなりません。建物\nの電気の復旧と共に、エレベーターも復旧します。", 
                "停電救出運転が行われている間は\n「停電管制運転中」\nと表示され、エレベーターが特別な運転をしている\nことが、乗っている人に伝えられます。", 
                "停電救出運転時は最寄りの階に停止し、扉が\n開きます。停止する最寄り階は乗客の人数\nによって、上の最寄り階か下の最寄り階かが\n決まります。", 
                "停電救出運転中は「停電」と表示され、\nエレベーターが特別な運転をしていることが、\nエレベーターの外の人にも伝えられます。"
            )
        );
        
        electTextSets.Add(
            LoadType.Medium, 
            new UiTextSet(
                "停電時は、最寄り階までエレベーターが走行し、扉を一定時間\n開きます。止まる階は乗客の人数によって、上の最寄り階か、\n下の最寄り階かが決まります。かご内の天井の明かりは停電灯\nに切り替わり、真っ暗にはなりません。\n建物の電気の復旧と共に、エレベーターも復旧します。", 
                "停電救出運転が行われている間は\n「停電管制運転中」と表示されます。", 
                "停電救出運転時は最寄りの階に停止します。乗客の\n人数によって、上の最寄り階か下の最寄り階かが\n決まります。", 
                "停電救出運転中は「停電」と表示されます。"
            )
        );

        electTextSets.Add(
            LoadType.Low, 
            new UiTextSet(
                "停電時は、最寄り階までエレベーターが走行し、扉を一定時間\n開きます。かご内の天井の明かりは停電灯に切り替わり、\n真っ暗になることはありません。", 
                "停電時は、特別な表示になります。", 
                "停電時は、最寄りの階に停止します。", 
                "停電時は，特別な表示になります。"
            )
        );

        electTextSets.Add(
            LoadType.None, 
            new UiTextSet(
                " ", 
                " ", 
                " ", 
                " "
            )
        );
    }
}

