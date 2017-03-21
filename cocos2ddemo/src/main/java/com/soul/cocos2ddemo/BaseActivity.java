package com.soul.cocos2ddemo;

import android.os.Bundle;
import android.support.annotation.Nullable;
import android.support.v7.app.AppCompatActivity;
import android.view.WindowManager;

import com.soul.library.utils.DisplayUtil;

/**
 * * @author soul
 *
 * @项目名:Game
 * @包名: com.soul.cocos2ddemo
 * @作者：祝明
 * @描述：TODO
 * @创建时间：2017/3/18 11:35
 */

public class BaseActivity extends AppCompatActivity {

    @Override
    public void onCreate(@Nullable Bundle savedInstanceState) {
        super.onCreate(savedInstanceState);
        //隐藏底部虚拟键
        DisplayUtil.hideNativeButton(this);
        //保持常亮
        getWindow().addFlags(WindowManager.LayoutParams.FLAG_KEEP_SCREEN_ON);
    }
}
