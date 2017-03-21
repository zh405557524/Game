package com.soul.cocos2ddemo;

import android.os.Bundle;

import org.cocos2d.layers.CCScene;
import org.cocos2d.nodes.CCDirector;
import org.cocos2d.opengl.CCGLSurfaceView;

public class MainActivity extends BaseActivity {
    private CCDirector mCcDirector;
    /**
     *
     *SurfaceView
     * SurfaceViewHolder
     * SurfaceViewHolder.CallBack
     *
     * @param savedInstanceState
     */

    /**CCDirector
     * CCScene
     * CCLayer
     * CCSprite
     *
     * @param savedInstanceState
     */
    @Override
    public void onCreate(Bundle savedInstanceState) {
        super.onCreate(savedInstanceState);

        //1 创建一个CCGLSurfaceView
        CCGLSurfaceView ccglSurfaceView = new CCGLSurfaceView(this);
        //2 创建导演
        //单例的设计模式，说明只有一个导演对象
        mCcDirector = CCDirector.sharedDirector();
        //3 导演关联CCGLSurfaceView，启动线程，开始绘制。
        mCcDirector.attachInView(ccglSurfaceView);
        //4 导演类进行常规的设置
        //4-1 设置屏幕横屏
        mCcDirector.setDeviceOrientation(CCDirector.kCCDeviceOrientationLandscapeLeft);
        //4-2 设置可以显示帧率(没秒显示多少桢)
        mCcDirector.setDisplayFPS(true);
        //4-3 设置屏幕适配
        mCcDirector.setScreenSize(480,320);//这里指的不是分辨率
        //4 -4
        //5 创建场景
        CCScene ccScene  = CCScene.node();//CCScene 不是单例

        //6 创建布景
//        FirstLayer firstLayer = new FirstLayer();
        ActionLayer actionLayer = new ActionLayer();
        //添加布景到场景
        ccScene.addChild(actionLayer);


        //7 导演要运行场景
        mCcDirector.runWithScene(ccScene);

        setContentView(ccglSurfaceView);


    }

    @Override
    protected void onResume() {
        super.onResume();
        mCcDirector.onResume();
    }

    @Override
    protected void onPause() {
        super.onPause();
        mCcDirector.onPause();
    }

    @Override
    protected void onDestroy() {
        super.onDestroy();
        mCcDirector.end();
    }
}
